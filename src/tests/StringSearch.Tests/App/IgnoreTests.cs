using StringSearch.App;
using StringSearch.Editing;
using StringSearch.Model;
using StringSearch.Persistence;
using StringSearch.Scanning;
using StringSearch.Tui;

namespace StringSearch.Tests.App;

public class IgnoreTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private (Session Session, SearchStore Store) Open()
    {
        var scan = StringScanner.ScanDirectory(_dir.Path);
        var store = SearchStore.Load(_dir.Path);
        var filtered = CommittedFilter.Apply(scan.Occurrences, store.Entries);
        return (new Session(filtered.Visible, scan, filtered.Hidden.Count, new SourceCache()), store);
    }

    private static ConsoleKeyInfo Key(ConsoleKey key, char c = '\0') => new(c, key, false, false, false);
    private static ConsoleKeyInfo Char(char c) => Key(ConsoleKey.NoName, c);

    [Theory]
    [InlineData('i')]
    [InlineData('I')]
    public void I_in_scroll_mode_requests_an_ignore_toggle(char key)
    {
        var items = StringScanner.ScanSource("class A { string a = \"a\"; }", "A.cs", "A.cs");
        var sut = new Controller(items, _ => SourceDocument.FromContent("A.cs", "class A { string a = \"a\"; }"));

        var state = sut.Handle(sut.Handle(AppState.Initial, Char('1'), 5), Char(key), 5);

        Assert.Equal((Mode.Scroll, Effect.ToggleIgnore), (state.Mode, state.Effect));
    }

    [Fact]
    public void I_does_nothing_in_waiting_mode_and_is_typed_in_edit_mode()
    {
        const string code = "class A { string a = \"a\"; }";
        var items = StringScanner.ScanSource(code, "A.cs", "A.cs");
        var sut = new Controller(items, _ => SourceDocument.FromContent("A.cs", code));

        Assert.Equal(Effect.None, sut.Handle(AppState.Initial, Char('i'), 5).Effect);

        var editing = sut.Handle(sut.Handle(AppState.Initial, Char('2'), 5), Char('I'), 5);
        Assert.Equal(Effect.None, editing.Effect);
        Assert.Contains("\"Ia\"", editing.Edit!.Text.ToString());
    }

    [Fact]
    public void Ignoring_moves_on_to_the_next_string_and_restoring_stays()
    {
        // Strings on lines 2, 20 and 40 of a 50-line file.
        var code = string.Join("\n", Enumerable.Range(1, 50).Select(i => i is 2 or 20 or 40 ? $"var s{i} = \"v{i}\";" : "//"));
        var items = StringScanner.ScanSource(code, "A.cs", "A.cs");
        var sut = new Controller(items, _ => SourceDocument.FromContent("A.cs", code));
        var state = new AppState(Mode.Scroll, 0, 0);

        var next = sut.AfterIgnoreToggle(state with { Effect = Effect.ToggleIgnore }, ignored: true, "Ignored \"v2\"", 10);
        Assert.Equal((1, Effect.None), (next.Index, next.Effect));
        Assert.Equal(sut.Focus(state, 1, 10).TopLine, next.TopLine); // centered on the next string
        Assert.Equal("Ignored \"v2\"; ← goes back to it", next.Message);

        var restored = sut.AfterIgnoreToggle(state, ignored: false, "Restored \"v2\"", 10);
        Assert.Equal((0, "Restored \"v2\""), (restored.Index, restored.Message));

        var last = sut.AfterIgnoreToggle(state with { Index = 2 }, ignored: true, "Ignored \"v40\"", 10);
        Assert.Equal((2, "Ignored \"v40\"; press I again to restore it"), (last.Index, last.Message));
    }

    // Five strings on lines 2, 4, 6, 8, 10; statuses set per test.
    private static readonly string FiveCode = string.Join("\n", Enumerable.Range(1, 12).Select(i => i % 2 == 0 && i <= 10 ? $"var s{i} = \"v{i}\";" : "//"));
    private static readonly IReadOnlyList<StringOccurrence> Five = StringScanner.ScanSource(FiveCode, "A.cs", "A.cs");

    private static Controller FiveController(params ItemStatus[] statuses) =>
        new(Five, _ => SourceDocument.FromContent("A.cs", FiveCode), o => statuses[Five.ToList().IndexOf(o)]);

    private static ConsoleKeyInfo Tab => Key(ConsoleKey.Tab, '\t');

    [Fact]
    public void Ctrl_i_toggles_live_only_and_arrows_skip_ignored_and_committed()
    {
        var sut = FiveController(ItemStatus.Live, ItemStatus.Ignored, ItemStatus.Committed, ItemStatus.Live, ItemStatus.Ignored);
        var state = sut.Handle(AppState.Initial, Char('1'), 5);

        state = sut.Handle(state, Tab, 5);
        Assert.True(state.LiveOnly);
        Assert.Equal(Controller.LiveOnlyOn, state.Message);

        state = sut.Handle(state, Key(ConsoleKey.RightArrow), 5);
        Assert.Equal(3, state.Index); // skipped 1 (ignored) and 2 (committed)

        var stuck = sut.Handle(state, Key(ConsoleKey.RightArrow), 5);
        Assert.Equal(3, stuck.Index); // 4 is ignored: nothing live ahead
        Assert.Equal("No live strings ahead (Ctrl+I steps through all)", stuck.Message);

        state = sut.Handle(state, Key(ConsoleKey.LeftArrow), 5);
        Assert.Equal(0, state.Index);

        state = sut.Handle(state, Tab, 5);
        Assert.False(state.LiveOnly);
        Assert.Equal(Controller.LiveOnlyOff, state.Message);
        Assert.Equal(1, sut.Handle(state, Key(ConsoleKey.RightArrow), 5).Index); // steps through everything again
    }

    [Fact]
    public void Real_ctrl_i_chord_also_toggles()
    {
        var sut = FiveController(ItemStatus.Live, ItemStatus.Live, ItemStatus.Live, ItemStatus.Live, ItemStatus.Live);
        var state = sut.Handle(AppState.Initial, Char('1'), 5);

        Assert.True(sut.Handle(state, new ConsoleKeyInfo('\t', ConsoleKey.I, false, false, true), 5).LiveOnly);
    }

    [Fact]
    public void Tab_is_still_typed_in_edit_mode()
    {
        var sut = FiveController(ItemStatus.Live, ItemStatus.Live, ItemStatus.Live, ItemStatus.Live, ItemStatus.Live);
        var state = sut.Handle(AppState.Initial, Char('2'), 5);

        state = sut.Handle(state, Tab, 5);

        Assert.False(state.LiveOnly);
        Assert.Contains("\"\tv2\"", state.Edit!.Text.ToString());
    }

    [Fact]
    public void Ignoring_in_live_only_mode_moves_to_the_next_live_string()
    {
        var sut = FiveController(ItemStatus.Ignored, ItemStatus.Committed, ItemStatus.Live, ItemStatus.Ignored, ItemStatus.Ignored);
        var state = new AppState(Mode.Scroll, 0, 0, LiveOnly: true);

        Assert.Equal(2, sut.AfterIgnoreToggle(state, ignored: true, "Ignored \"v2\"", 5).Index);

        var none = sut.AfterIgnoreToggle(state with { Index = 2 }, ignored: true, "Ignored \"v6\"", 5);
        Assert.Equal(2, none.Index); // nothing live after it: stay
        Assert.EndsWith("press I again to restore it", none.Message);
    }

    [Fact]
    public void Live_only_shows_in_the_mode_badge()
    {
        var session = new Session(Five, new ScanResult(Five, ["A.cs"], []), 0, new SourceCache(p => SourceDocument.FromContent(p, FiveCode)));

        var frame = Screen.Render(session, new AppState(Mode.Scroll, 0, 0, LiveOnly: true), 100, 12);

        Assert.Contains(" SCROLL · LIVE ONLY ", frame.RowText(1));
    }

    [Fact]
    public void Ignored_string_cannot_be_edited()
    {
        const string code = "class A { string a = \"a\"; }";
        var items = StringScanner.ScanSource(code, "A.cs", "A.cs");
        var sut = new Controller(items, _ => SourceDocument.FromContent("A.cs", code), _ => ItemStatus.Ignored);

        var state = sut.Handle(sut.Handle(AppState.Initial, Char('1'), 5), Key(ConsoleKey.Enter, '\r'), 5);

        Assert.Equal(Mode.Scroll, state.Mode);
        Assert.Equal(Controller.IgnoredNotEditable, state.Message);
    }

    [Fact]
    public void Ignoring_records_an_ignored_snippet_and_hides_the_string_next_run()
    {
        _dir.Write("A.cs", "class A\n{\n    string a = \"a\";\n    string b = \"b\";\n}\n");
        var (session, store) = Open();

        var result = Ignorer.Toggle(session, store, 0, Now);

        Assert.Equal(new IgnoreResult(true, "Ignored \"a\""), result);
        Assert.True(session.IsIgnored(0));
        Assert.Equal(1, session.IgnoredCount);
        var snippet = Assert.Single(SearchStore.Load(_dir.Path).Entries[new SearchKey("A.cs", "a")]);
        Assert.Equal(SnippetKind.Ignored, snippet.Kind);
        Assert.Contains("\"kind\": \"ignored\"", File.ReadAllText(Path.Combine(_dir.Path, ".searches")));

        var (next, _) = Open();
        Assert.Equal(["b"], next.Items.Select(o => o.Value));
        Assert.Equal(1, next.HiddenCount);
    }

    [Fact]
    public void Pressing_I_again_restores_the_string_and_removes_the_snippet()
    {
        _dir.Write("A.cs", "class A { string a = \"a\"; }");
        var (session, store) = Open();
        Ignorer.Toggle(session, store, 0, Now);

        var result = Ignorer.Toggle(session, store, 0, Now);

        Assert.Equal(new IgnoreResult(false, "Restored \"a\""), result);
        Assert.False(session.IsIgnored(0));
        Assert.Empty(SearchStore.Load(_dir.Path).Entries);
        Assert.Single(Open().Session.Items);
    }

    [Fact]
    public void Committed_strings_cannot_be_ignored()
    {
        _dir.Write("A.cs", "class A { string a = \"a\"; }");
        var (session, store) = Open();
        session.MarkCommitted(0);

        var result = Ignorer.Toggle(session, store, 0, Now);

        Assert.False(result.Ignored);
        Assert.StartsWith("Already committed", result.Message);
        Assert.True(session.IsCommitted(0));
        Assert.False(File.Exists(Path.Combine(_dir.Path, ".searches")));
    }

    [Fact]
    public void Committed_snippets_without_a_kind_still_load_as_committed()
    {
        _dir.Write(".searches", """
            { "version": 1, "entries": [ { "file": "A.cs", "value": "a", "snippets": [
              { "before": "", "text": "x", "after": "", "committedUtc": "2026-11-14T10:00:00Z" } ] } ] }
            """);

        var store = SearchStore.Load(_dir.Path);

        Assert.Null(store.LoadWarning);
        Assert.Equal(SnippetKind.Committed, Assert.Single(store.Entries[new SearchKey("A.cs", "a")]).Kind);
    }

    [Fact]
    public void Ignored_gray_differs_from_committed_gray_and_the_header_says_ignored()
    {
        const string code = "class A\n{\n    string s = \"hi\";\n}\n";
        var occurrences = StringScanner.ScanSource(code, "A.cs", "A.cs");
        var session = new Session(occurrences, new ScanResult(occurrences, ["A.cs"], []), 0,
            new SourceCache(p => SourceDocument.FromContent(p, code)));
        session.SetStatus(0, ItemStatus.Ignored);

        var frame = Screen.Render(session, new AppState(Mode.Scroll, 0, 0), 80, 12);

        int row = 6, keyword = frame.RowText(row).IndexOf("string", StringComparison.Ordinal);
        int literal = frame.RowText(row).IndexOf("\"hi\"", StringComparison.Ordinal);
        Assert.Equal(Theme.Ignored, frame[row, keyword].Style);
        Assert.Equal(Theme.IgnoredString, frame[row, literal].Style);
        Assert.NotEqual(Theme.Committed.Fg, Theme.Ignored.Fg);
        Assert.NotEqual(Theme.CommittedString, Theme.IgnoredString);
        Assert.Contains("⊘ ignored", frame.RowText(1));
        Assert.Contains("0 committed · 1 ignored this session", frame.RowText(2));

        session.SetStatus(0, ItemStatus.Live);
        frame = Screen.Render(session, new AppState(Mode.Scroll, 0, 0), 80, 12);
        Assert.Equal(Theme.Keyword, frame[row, keyword].Style); // colors are back
    }

    [Fact]
    public void Commit_in_the_same_file_keeps_an_ignored_neighbor_ignored_and_refreshes_its_snippet()
    {
        _dir.Write("A.cs", "class A\n{\n    string a = \"a\";\n    string b = \"b\";\n}\n");
        var (session, store) = Open();
        Ignorer.Toggle(session, store, 0, Now); // "a"; its "after" line is b's line

        var b = session.Items[1];
        var edit = EditSession.Start(session.Sources.Get(b.FilePath), b).Delete().Insert("B");
        var result = Committer.Commit(session, store, 1, edit, Now);

        Assert.True(result.Success, result.Message);
        Assert.True(session.IsIgnored(0));
        Assert.True(session.IsCommitted(1));
        var snippet = Assert.Single(SearchStore.Load(_dir.Path).Entries[new SearchKey("A.cs", "a")]);
        Assert.Equal((SnippetKind.Ignored, "string b = \"B\";"), (snippet.Kind, snippet.Context.After));
        Assert.Empty(Open().Session.Items);
    }
}
