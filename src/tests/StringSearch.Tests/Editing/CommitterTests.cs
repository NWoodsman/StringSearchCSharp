using System.Text;
using StringSearch.App;
using StringSearch.Editing;
using StringSearch.Model;
using StringSearch.Persistence;
using StringSearch.Scanning;
using StringSearch.Tui;

namespace StringSearch.Tests.Editing;

public class CommitterTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private (Session Session, SearchStore Store) Open()
    {
        var scan = StringScanner.ScanDirectory(_dir.Path);
        var store = SearchStore.Load(_dir.Path);
        var filtered = CommittedFilter.Apply(scan.Occurrences, store.Entries);
        return (new Session(filtered.Visible, scan, filtered.Ignored.Count, new SourceCache()), store);
    }

    private static int IndexOf(Session session, string value) =>
        session.Items.Select((o, i) => (o, i)).Single(p => p.o.Value == value).i;

    /// <summary>Replaces the whole content of the string at <paramref name="index"/>.</summary>
    private static EditSession Retype(Session session, int index, string newValue)
    {
        var occurrence = session.Items[index];
        var edit = EditSession.Start(session.Sources.Get(occurrence.FilePath), occurrence);
        for (int i = 0; i < occurrence.Value.Length; i++)
            edit = edit.Delete();
        return edit.Insert(newValue);
    }

    private CommitResult CommitRetype(Session session, SearchStore store, string oldValue, string newValue)
    {
        int index = IndexOf(session, oldValue);
        return Committer.Commit(session, store, index, Retype(session, index, newValue), Now);
    }

    [Fact]
    public void Writes_the_file_marks_the_string_committed_and_saves_searches()
    {
        var path = _dir.Write("src/A.cs", "class A\n{\n    string a = \"hello\";\n    string b = \"other\";\n}\n");
        var (session, store) = Open();

        var result = CommitRetype(session, store, "hello", "howdy!");

        Assert.True(result.Success, result.Message);
        Assert.Equal("class A\n{\n    string a = \"howdy!\";\n    string b = \"other\";\n}\n", File.ReadAllText(path));
        Assert.Equal("Committed \"hello\" → \"howdy!\"", result.Message);

        Assert.Equal(["howdy!", "other"], session.Items.Select(o => o.Value));
        Assert.Equal(0, result.Index);
        Assert.True(session.IsCommitted(0));
        Assert.False(session.IsCommitted(1));
        Assert.Equal(1, session.CommittedCount);
        // The later string's span shifted by one character, and the list knows it.
        Assert.Equal("\"other\"", session.Sources.Get(path).Text.ToString(session.Items[1].Span));

        var saved = SearchStore.Load(_dir.Path);
        var snippet = Assert.Single(saved.Entries[new SearchKey("src/A.cs", "howdy!")]);
        Assert.Equal(new SnippetContext("{", "string a = \"howdy!\";", "string b = \"other\";"), snippet.Context);
        Assert.Equal(Now, snippet.CommittedUtc);
    }

    [Fact]
    public void Next_run_hides_the_committed_string_only()
    {
        _dir.Write("A.cs", "class A { string a = \"hello\"; string b = \"other\"; }");
        var (session, store) = Open();
        CommitRetype(session, store, "hello", "howdy");

        var (next, _) = Open();

        Assert.Equal(["other"], next.Items.Select(o => o.Value));
        Assert.Equal(1, next.IgnoredCount);
    }

    [Fact]
    public void Preserves_utf8_bom_and_crlf()
    {
        var path = Path.Combine(_dir.Path, "A.cs");
        File.WriteAllText(path, "class A\r\n{\r\n    string a = \"héllo\";\r\n}\r\n", new UTF8Encoding(true));
        var (session, store) = Open();

        CommitRetype(session, store, "héllo", "wörld");

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("class A\r\n{\r\n    string a = \"wörld\";\r\n}\r\n", Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public void Preserves_utf16_and_unix_permissions()
    {
        var path = Path.Combine(_dir.Path, "A.cs");
        File.WriteAllText(path, "class A { string a = \"x\"; }", new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        var (session, store) = Open();

        CommitRetype(session, store, "x", "y");

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 0xFF, 0xFE }, bytes[..2]);
        Assert.Equal("class A { string a = \"y\"; }", Encoding.Unicode.GetString(bytes[2..]));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead, File.GetUnixFileMode(path));
        Assert.Empty(Directory.GetFiles(_dir.Path, "*.tmp"));
    }

    [Fact]
    public void Refuses_when_the_file_changed_on_disk()
    {
        var path = _dir.Write("A.cs", "class A { string a = \"hello\"; }");
        var (session, store) = Open();
        var edit = Retype(session, 0, "mine");
        File.WriteAllText(path, "class A { string a = \"theirs\"; }");

        var result = Committer.Commit(session, store, 0, edit, Now);

        Assert.False(result.Success);
        Assert.Contains("changed on disk", result.Message);
        Assert.Equal("class A { string a = \"theirs\"; }", File.ReadAllText(path));
        Assert.False(File.Exists(Path.Combine(_dir.Path, ".searches")));
        Assert.False(session.IsCommitted(0));
    }

    [Fact]
    public void Editing_a_committed_string_again_replaces_its_snippet()
    {
        _dir.Write("A.cs", "class A { string a = \"a\"; }");
        var (session, store) = Open();
        CommitRetype(session, store, "a", "b");

        var result = CommitRetype(session, store, "b", "c");

        Assert.True(result.Success, result.Message);
        var saved = SearchStore.Load(_dir.Path);
        Assert.Equal([new SearchKey("A.cs", "c")], saved.Entries.Keys);
        Assert.Equal(1, session.CommittedCount);
    }

    [Fact]
    public void Strings_hidden_by_searches_stay_hidden_after_a_commit_in_their_file()
    {
        _dir.Write("A.cs", "class A\n{\n    string x = \"x\";\n    string y = \"y\";\n}\n");
        var (first, firstStore) = Open();
        CommitRetype(first, firstStore, "x", "x2");

        var (session, store) = Open(); // x2 is hidden now
        Assert.Equal(["y"], session.Items.Select(o => o.Value));

        var result = CommitRetype(session, store, "y", "y2");

        Assert.Equal(["y2"], session.Items.Select(o => o.Value));
        Assert.Equal(0, result.Index);
        Assert.Equal(2, SearchStore.Load(_dir.Path).Entries.Count);
    }

    [Fact]
    public void A_new_string_typed_in_the_edit_is_listed_but_not_committed()
    {
        _dir.Write("A.cs", "class A { string a = \"a\"; }");
        var (session, store) = Open();
        var occurrence = session.Items[0];
        var edit = EditSession.Start(session.Sources.Get(occurrence.FilePath), occurrence)
            .Right().Right().Insert(" + \"extra\"");

        var result = Committer.Commit(session, store, 0, edit, Now);

        Assert.True(result.Success, result.Message);
        Assert.Equal(["a", "extra"], session.Items.Select(o => o.Value));
        Assert.True(session.IsCommitted(0));
        Assert.False(session.IsCommitted(1));
    }

    [Fact]
    public void Deleting_the_string_removes_it_from_the_list()
    {
        var path = _dir.Write("A.cs", "class A { string a = \"a\"; string b = \"b\"; }");
        var (session, store) = Open();
        var occurrence = session.Items[0];
        var edit = EditSession.Start(session.Sources.Get(path), occurrence) with { Cursor = occurrence.Span.Start };
        for (int i = 0; i < 3; i++)
            edit = edit.Delete();
        edit = edit.Insert("null");

        var result = Committer.Commit(session, store, 0, edit, Now);

        Assert.True(result.Success, result.Message);
        Assert.Contains("the string was removed", result.Message);
        Assert.Equal(["b"], session.Items.Select(o => o.Value));
        Assert.Equal(0, result.Index);
        Assert.Empty(SearchStore.Load(_dir.Path).Entries);
    }

    [Fact]
    public void Committed_neighbor_snippet_follows_a_context_change()
    {
        _dir.Write("A.cs", "class A\n{\n    string a = \"a\";\n    string b = \"b\";\n}\n");
        var (session, store) = Open();
        CommitRetype(session, store, "a", "a2"); // its "after" line is b's line

        CommitRetype(session, store, "b", "b2");

        var saved = SearchStore.Load(_dir.Path);
        Assert.Equal("string b = \"b2\";", Assert.Single(saved.Entries[new SearchKey("A.cs", "a2")]).Context.After);
        var (next, _) = Open();
        Assert.Empty(next.Items);
    }

    [Fact]
    public void Other_files_keep_their_items_and_marks()
    {
        _dir.Write("A.cs", "class A { string a = \"a\"; }");
        _dir.Write("B.cs", "class B { string b = \"b\"; }");
        var (session, store) = Open();
        CommitRetype(session, store, "b", "b2");

        var result = CommitRetype(session, store, "a", "a2");

        Assert.Equal(["a2", "b2"], session.Items.Select(o => o.Value));
        Assert.Equal(0, result.Index);
        Assert.True(session.IsCommitted(0) && session.IsCommitted(1));
    }

    [Theory]
    [InlineData(0, 5, 0)]       // before the patch: unchanged
    [InlineData(5, 5, 5)]       // ends exactly where the patch starts: unchanged
    [InlineData(12, 3, 20)]     // starts exactly where the patch ends: shifted by +8
    [InlineData(20, 5, 28)]     // after: shifted by +8
    [InlineData(9, 4, null)]    // overlaps the patch: no mapping
    public void MapStart_shifts_only_spans_clear_of_the_patch(int spanStart, int spanLength, int? expected)
    {
        // Replaces [10, 12) with 10 characters: +8.
        var patch = new Microsoft.CodeAnalysis.Text.TextChange(new Microsoft.CodeAnalysis.Text.TextSpan(10, 2), "ABCDEFGHIJ");

        Assert.Equal(expected, Committer.MapStart(new Microsoft.CodeAnalysis.Text.TextSpan(spanStart, spanLength), patch));
    }
}

public class FileWriterAndSyntaxCheckTests
{
    [Theory]
    [InlineData("UTF-8", new byte[] { 0x61 })]
    [InlineData("UTF-8 BOM", new byte[] { 0xEF, 0xBB, 0xBF, 0x61 })]
    [InlineData("UTF-16 LE", new byte[] { 0xFF, 0xFE, 0x61, 0 })]
    [InlineData("UTF-16 BE", new byte[] { 0xFE, 0xFF, 0, 0x61 })]
    public void Encode_writes_the_matching_preamble(string encoding, byte[] expected) =>
        Assert.Equal(expected, FileWriter.Encode("a", encoding));

    [Fact]
    public void Commit_prompt_warns_when_the_edit_adds_syntax_errors()
    {
        const string code = "class A { string a = \"x\"; }";
        var occurrence = StringScanner.ScanSource(code).Single();
        var edit = EditSession.Start(SourceDocument.FromContent("A.cs", code), occurrence);

        var broken = edit.Insert("\"");
        var fine = edit.Insert("y");

        Assert.True(broken.AddsSyntaxErrors);
        Assert.False(fine.AddsSyntaxErrors);
        Assert.Contains("syntax errors", Screen.PromptText(StringSearch.App.Prompt.ConfirmCommit, broken));
        Assert.DoesNotContain("syntax errors", Screen.PromptText(StringSearch.App.Prompt.ConfirmCommit, fine));
    }
}
