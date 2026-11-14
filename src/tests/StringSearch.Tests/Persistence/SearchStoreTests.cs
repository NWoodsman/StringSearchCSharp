using StringSearch.Model;
using StringSearch.Persistence;

namespace StringSearch.Tests.Persistence;

public class SearchStoreTests
{
    private static readonly DateTime When = new(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);

    private static Snippet Snip(string text, string before = "b", string after = "a") =>
        new(new SnippetContext(before, text, after), When);

    [Fact]
    public void Missing_file_gives_empty_store_without_warning()
    {
        using var dir = new TempDir();

        var store = SearchStore.Load(dir.Path);

        Assert.Empty(store.Entries);
        Assert.Null(store.LoadWarning);
    }

    [Fact]
    public void Round_trips_awkward_strings_and_multiple_snippets_per_entry()
    {
        using var dir = new TempDir();
        var store = SearchStore.Load(dir.Path);
        var tricky = new SearchKey("src/ü/A.cs", "line1\nline2 \"quoted\" \\ tab\t émoji 🎉");
        store.Add(tricky, Snip("x = \"...\";"));
        store.Add(new SearchKey("src/A.cs", "OK"), Snip("f(\"OK\");", before: "one"));
        store.Add(new SearchKey("src/A.cs", "OK"), Snip("f(\"OK\");", before: "two"));
        store.Save();

        var loaded = SearchStore.Load(dir.Path);

        Assert.Null(loaded.LoadWarning);
        Assert.Equal(2, loaded.Entries.Count);
        Assert.Equal(store.Entries[tricky], loaded.Entries[tricky]);
        Assert.Equal(["one", "two"], loaded.Entries[new SearchKey("src/A.cs", "OK")].Select(s => s.Context.Before));
    }

    [Fact]
    public void Keys_differing_by_one_character_are_separate_entries()
    {
        using var dir = new TempDir();
        var store = SearchStore.Load(dir.Path);
        store.Add(new SearchKey("A.cs", "Hello"), Snip("1"));
        store.Add(new SearchKey("A.cs", "Hello!"), Snip("2"));
        store.Add(new SearchKey("A.cs", "hello"), Snip("3"));

        Assert.Equal(3, store.Entries.Count);
    }

    [Fact]
    public void Saved_json_has_expected_shape_and_no_line_numbers()
    {
        using var dir = new TempDir();
        var store = SearchStore.Load(dir.Path);
        store.Add(new SearchKey("src/Foo.cs", "Hello"), Snip("Console.WriteLine(\"Hello\");", "var name = GetName();", "return;"));
        store.Save();

        var json = File.ReadAllText(Path.Combine(dir.Path, ".searches"));

        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"file\": \"src/Foo.cs\"", json);
        Assert.Contains("\"value\": \"Hello\"", json);
        Assert.Contains("\"before\": \"var name = GetName();\"", json);
        Assert.Contains("\"committedUtc\": \"2026-11-14T10:00:00Z\"", json);
        Assert.DoesNotContain("\"line\"", json, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(dir.Path, ".searches.tmp")));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("""{ "version": 99, "entries": [] }""")]
    [InlineData("""{ "version": 1, "entries": [ { "file": "A.cs", "value": null, "snippets": [] } ] }""")]
    [InlineData("""{ "version": 1 }""")]
    public void Unreadable_file_is_moved_to_bak_and_store_starts_empty(string content)
    {
        using var dir = new TempDir();
        var path = dir.Write(".searches", content);

        var store = SearchStore.Load(dir.Path);

        Assert.Empty(store.Entries);
        Assert.NotNull(store.LoadWarning);
        Assert.False(File.Exists(path));
        Assert.Equal(content, File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void Remove_prefers_exact_context_and_drops_empty_keys()
    {
        using var dir = new TempDir();
        var store = SearchStore.Load(dir.Path);
        var key = new SearchKey("A.cs", "OK");
        store.Add(key, Snip("f(\"OK\");", before: "x", after: "same"));
        store.Add(key, Snip("f(\"OK\");", before: "y", after: "same"));

        Assert.True(store.Remove(key, new SnippetContext("y", "f(\"OK\");", "same")));
        Assert.Equal("x", Assert.Single(store.Entries[key]).Context.Before);

        Assert.False(store.Remove(key, new SnippetContext("p", "other", "q")));
        Assert.True(store.Remove(key, new SnippetContext("z", "f(\"OK\");", "same")));
        Assert.False(store.Entries.ContainsKey(key));
    }
}
