using StringSearch.Model;
using StringSearch.Scanning;

namespace StringSearch.Tests.Scanning;

public class CommittedFilterTests
{
    private static readonly DateTime When = new(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<StringOccurrence> Scan(string code, string file = "A.cs") =>
        StringScanner.ScanSource(code, file, file);

    private static Dictionary<SearchKey, List<Snippet>> Commit(params StringOccurrence[] occurrences)
    {
        var committed = new Dictionary<SearchKey, List<Snippet>>();
        foreach (var o in occurrences)
        {
            if (!committed.TryGetValue(o.Key, out var list))
                committed[o.Key] = list = [];
            list.Add(new Snippet(o.Context, When));
        }
        return committed;
    }

    [Fact]
    public void Only_the_committed_copy_of_a_repeated_value_is_hidden()
    {
        var found = Scan("class C {\nvoid A() {\nf(\"OK\");\n}\nvoid B() {\nf(\"OK\");\n}\n}");

        var result = CommittedFilter.Apply(found, Commit(found[1]));

        Assert.Equal([found[0]], result.Visible);
        Assert.Equal([found[1]], result.Hidden);
    }

    [Fact]
    public void Committed_string_stays_hidden_after_lines_are_inserted_above()
    {
        var before = Scan("class C {\nint x;\nstring s = \"hi\";\n}");
        var after = Scan("// header\nusing System;\n\nclass C {\nint x;\nstring s = \"hi\";\n}");

        var result = CommittedFilter.Apply(after, Commit(before[0]));

        Assert.Empty(result.Visible);
    }

    [Fact]
    public void One_changed_neighbor_still_matches_but_two_do_not()
    {
        var original = Scan("class C {\nint x;\nstring s = \"hi\";\nint y;\n}");
        var oneChanged = Scan("class C {\nint CHANGED;\nstring s = \"hi\";\nint y;\n}");
        var bothChanged = Scan("class C {\nint CHANGED;\nstring s = \"hi\";\nint ALSO;\n}");
        var committed = Commit(original[0]);

        Assert.Empty(CommittedFilter.Apply(oneChanged, committed).Visible);
        Assert.Single(CommittedFilter.Apply(bothChanged, committed).Visible);
    }

    [Fact]
    public void Changed_literal_line_is_shown_again()
    {
        var original = Scan("class C {\nint x;\nstring s = \"hi\";\n}");
        var edited = Scan("class C {\nint x;\nstring t = \"hi\";\n}");

        Assert.Single(CommittedFilter.Apply(edited, Commit(original[0])).Visible);
    }

    [Fact]
    public void Identical_duplicates_are_paired_one_to_one_in_file_order()
    {
        var found = Scan("class C {\nvoid M() {\nf(\"OK\");\nf(\"OK\");\nf(\"OK\");\nf(\"OK\");\n}\n}");
        // Copies 1 and 2 have the same context (before "f(\"OK\");", after "f(\"OK\");").
        Assert.Equal(found[1].Context, found[2].Context);

        var result = CommittedFilter.Apply(found, Commit(found[2]));

        Assert.Equal([found[1]], result.Hidden);
        Assert.Equal(3, result.Visible.Count);
    }

    [Fact]
    public void Exact_matches_are_paired_before_loose_ones()
    {
        // Two copies share the line and the "after" neighbor; only copy 2 has the exact context.
        var found = Scan("class C {\nvoid M() {\nint a;\nf(\"OK\");\nint z;\nint b;\nf(\"OK\");\nint z;\n}\n}");
        Assert.True(found[0].Context.Matches(found[1].Context));

        var result = CommittedFilter.Apply(found, Commit(found[1]));

        Assert.Equal([found[1]], result.Hidden);
    }

    [Fact]
    public void Same_value_in_another_file_or_with_one_character_changed_is_not_hidden()
    {
        const string code = "class C {\nstring s = \"Hello\";\n}";
        var committed = Commit(Scan(code)[0]);

        Assert.Single(CommittedFilter.Apply(Scan(code, "B.cs"), committed).Visible);
        Assert.Single(CommittedFilter.Apply(Scan(code.Replace("Hello", "Hello!")), committed).Visible);
    }
}
