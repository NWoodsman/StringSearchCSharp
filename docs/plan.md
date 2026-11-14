# Implementation Plan

Plan for the app described in `docs/design.md`. Written 2026-11-14.

## Starting point

- `src/StringSearchCSharp/StringSearch.csproj`: net10.0 console app that references `Microsoft.CodeAnalysis.CSharp` 5.12.0.
- `src/StringSearchCSharp/Program.cs`: validates the directory argument, walks `*.cs` files (skipping `bin/` and `obj/`), parses each one with Roslyn and prints string literals and the text parts of interpolated strings.
- `src/tests/`: empty.

The Roslyn scanning works and will be kept. Everything else is new.

## Architecture

The app is split into layers so most of the logic can be tested without a terminal.

```
src/StringSearchCSharp/
  Program.cs                 arg parsing, wiring, top-level try/finally to restore the terminal
  Model/
    StringOccurrence.cs      one found string (file, span, line/col, raw text, value)
  Scanning/
    StringScanner.cs         Roslyn walk of one file -> StringOccurrence[]
    PathFilter.cs            bin/obj + /Tests?/ exclusion
  Persistence/
    SearchStore.cs           the .searches file: load, save, Dictionary<SearchKey, List<Snippet>>, snippet matching
  Editing/
    EditSession.cs           editable buffer for one file, cursor, change tracking (the patch)
    FileWriter.cs            writes the result back with the original encoding, BOM and line endings
  Tui/
    Terminal.cs              ANSI output, alternate screen, cursor, raw key input, resize
    Frame.cs                 grid of (char, style) cells; diffed against the previous frame and flushed
    Layout.cs                splits the window into header / snippet pane / status bar
    PositionBar.cs           braille progress bar + "XX/XXX"
    Highlighter.cs           Roslyn token -> color classification
    SnippetView.cs           renders the visible source lines, highlighting, the string's range, the cursor
    StatusBar.cs             bottom metadata bar
  App/
    AppState.cs              current mode, index, scroll offset, edit session, open prompt
    Controller.cs            (state, key) -> new state + effects; the modal state machine
src/tests/StringSearch.Tests/   xUnit project
```

Main loop: `read key -> Controller.Handle -> render Frame -> flush diff`. The controller never writes to the console, so state transitions can be unit tested.

## Data model

```csharp
record StringOccurrence(
    string FilePath,        // absolute path
    string RelativePath,    // relative to the target directory, used for display and filtering
    TextSpan Span,          // span of the whole literal expression in the file
    int Line, int Column,   // 1-based
    string RawText,         // source text, including quotes and prefixes
    string Value);          // the token's ValueText; part of the SearchKey
```

- **List**: `List<StringOccurrence>`, sorted by (RelativePath, Span.Start). Used for navigation and the position bar.
- **Committed index**: `Dictionary<SearchKey, List<Snippet>>`, loaded from and saved to `.searches`. `SearchKey` is a `record struct (string RelativePath, string Value)`. Its generated `GetHashCode` combines both fields, so the path and the string are hashed together at runtime. The value is one `Snippet` per committed occurrence. This lets the same string appear several times in one file while only the copies that were committed get skipped. See "Committed index semantics" below.

```csharp
record Snippet(
    string Before,          // previous non-blank line, trimmed
    string Text,            // the line(s) holding the literal, trimmed
    string After,           // next non-blank line, trimmed
    DateTime CommittedUtc);
```

### Scanning changes
- Interpolated strings: treat the whole `InterpolatedStringExpressionSyntax` as **one** occurrence instead of one per text part. Editing works on the whole literal, and one entry per string keeps the position count meaningful. `Value` is the text with holes kept as `{expr}`, and escaped braces stay doubled (`{{`), so a literal brace can't be confused with a hole. String literals inside a hole (`$"{Get("key")}"`) are separate results.
- The literal's kind (regular, `@` verbatim, raw, `u8`) is not tracked. Only the value matters.
- Skip interpolated text parts that the expression walk already covers (avoids duplicates).
- Parse files in parallel (`Parallel.ForEach`) and then sort. This is cheap and helps on large repos.

### Path filtering
- Always skip `bin/` and `obj/` (existing behavior). Also skip `.git/`.
- The test filter checks the **relative** path, so a target directory that itself sits under e.g. `~/Tests/` is not excluded entirely.
- The path is split into segments: every directory name, plus the file name without `.cs`. A file is skipped when **any** segment looks like a test name. "test" or "tests" has to be a whole word in the segment, so `Latest.cs`, `Contest/`, `Attestation.cs` and `Testing.cs` stay included. A segment is a test name when it:
  1. **is** `test` or `tests`, in any case: `tests/`, `Tests/`, `TEST/`
  2. **ends** with a PascalCase `Test`/`Tests`: `UnitTests/`, `FooTests.cs`, `ParserTest.cs`
  3. **ends** with a separator followed by test(s), any case: `MyApp.Tests/`, `foo_test.cs`, `api-tests/`
  4. **starts** with test(s) followed by an uppercase letter or a separator: `TestHelpers/`, `Tests.Integration/`, `test_utils.cs`

  As one regex, without `IgnoreCase`, so the PascalCase rules work:
  ```
  ^(?i:tests?)$ | (?<=[a-z0-9])Tests?$ | [._-](?i:tests?)$ | ^(?i:tests?)(?=[A-Z._-])
  ```
- `PathFilter` keeps the list of skipped files. The header shows the count, and `--list` prints them, so wrong matches are easy to see.

## `.searches` file

- Location: `<target>/.searches`.
- Format: JSON, so multi-line strings and quotes round-trip safely:
  ```json
  {
    "version": 1,
    "entries": [
      { "file": "src/Foo.cs", "value": "Hello",
        "snippets": [
          { "before": "var name = GetName();", "text": "Console.WriteLine(\"Hello\");",
            "after": "return;", "committedUtc": "2026-11-14T10:00:00Z" } ] } ]
  }
  ```
  The dictionary is built from these entries. `file` is the path relative to the target directory, with `/` separators so the file works across operating systems.
- Written atomically (write `.searches.tmp`, then `File.Move(overwrite: true)`) after each commit.
- At startup: if the file exists, load it and filter out occurrences that match a committed snippet (see below). If it is missing or corrupt, start with an empty set (a corrupt file is renamed to `.searches.bak` and a warning shown).

### Committed index semantics (decided)
The dictionary holds only occurrences the user has **committed**. Each key is file + value, and each key maps to one `Snippet` per committed copy.

An **entry** in `.searches` is one `(file, value)` key. Values are compared exactly (ordinal, character for character):
- A committed string that differs from another by even one character is a **new entry**.
- Committed strings in the same file that match **exactly** go into the **same entry**, as separate snippets. Two committed copies of `"OK"` in `Foo.cs` give one entry with two snippets.

**On commit** (non-empty diff), with `old` = `(file, oldValue)` and `new` = `(file, newValue)`:
1. Remove the snippet for this occurrence from `dict[old]`, if it is there (the string had been committed before). Drop the key once its list is empty.
2. Build a `Snippet` from the **edited** text around the new literal and append it to `dict[new]`.
3. Save `.searches`.

**At startup**, for each found occurrence whose `(file, value)` is in the dictionary:
- Compute its `Before/Text/After` the same way and compare them with the stored snippets. A snippet matches when `Text` is equal and at least one of `Before`/`After` is equal. Line numbers are not part of the match, so edits elsewhere in the file don't break it.
- Line numbers are **not stored** in `.searches`. They are calculated from the source while the file is scanned on load (Roslyn's line map), and they are only needed for display.
- If several occurrences have identical context (duplicated lines), they cannot be told apart without positions. They are paired with the stored snippets in file order, one-to-one: N matching snippets hide the first N of those occurrences. Each snippet hides at most one occurrence.
- A matched occurrence is skipped. An unmatched one is shown. The matching is deliberately conservative: when the code around a committed string changes a lot, the string comes back for review instead of being hidden by mistake.
- Snippets that match nothing are kept in the file (the code may be on another branch). Deleting `.searches` resets everything, and renaming a file makes its entries stop matching.

The same `Dictionary<SearchKey, List<StringOccurrence>>` lookup over the found strings is also useful at runtime, e.g. to show "3 more in this file" in the header.

## Terminal layer

Hand-written ANSI rendering instead of a TUI framework. The layout is fixed and small, Terminal.Gui would fight the custom editing behavior, and Spectre.Console's live display is not built for raw key-by-key editing. Details:

- Enter the alternate screen buffer (`ESC[?1049h`) and hide the cursor on start. Restore both in `finally`, on Ctrl+C (`Console.CancelKeyPress`) and on unhandled exceptions, so the user's terminal is never left broken.
- `Console.TreatControlCAsInput = true`; read keys with `Console.ReadKey(intercept: true)`.
- Render into a `Frame` (cell grid), diff it against the previous frame and write only the changed cells with absolute positioning (`ESC[row;colH`). Nothing scrolls the terminal.
- Poll `Console.WindowWidth/Height` on each loop iteration and on a short timer (`Console.KeyAvailable` loop with ~50 ms waits). On resize, do a full redraw.
- Colors: 24-bit truecolor, falling back to 256 colors when `COLORTERM` is not `truecolor`/`24bit`.
- Minimum size check (e.g. 40x10). Below that, show "terminal too small".

## Screen layout

```
┌ header ─────────────────────────────────────────────────────────┐
│ ⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⡇●                                    42/318 │  position bar
│ src/Services/Mailer.cs:118:24   SCROLL                         │  file:line:col, mode
│ done this session: 3   ignored (.searches): 57   skipped tests: 12 files │
├ snippet pane ───────────────────────────────────────────────────┤
│ 113 │     public void Send(Message m)                          │
│ 118 │         _log.Info("Sending message to {0}", m.To);       │  string range highlighted
│ ...                                                            │
├ status bar ─────────────────────────────────────────────────────┤
│ Ln 118 Col 24 │ Δ +5 −2 │ UTF-8 BOM │ CRLF │ EDIT │ ^S save  Esc back │
└─────────────────────────────────────────────────────────────────┘
```

### Position bar
- Bar width W = window width − width of the count label − 2.
- Filled length = `(index+1)/count × W`, at sub-cell resolution using braille fill levels per cell (`⠀ ⡀ ⡄ ⡆ ⡇ ⣇ ⣧ ⣷ ⣿`, 8 steps per cell), so the bar grows or shrinks visibly with every step even when there are thousands of strings.
- A marker glyph (`●`) at the tip shows the current position.
- Color is interpolated along a gradient by fraction (e.g. cool blue at the start → amber → green near the end), so how close the user is to the end can be seen at a glance.
- Label `XX/XXX`, padded to the width of the total so the bar does not jitter.
- `PositionBar.Render(index, count, width)` returns cells. It is a pure function and is unit tested.

### Status bar contents
Cursor `Ln/Col` (in edit mode; the string's start position in scroll mode), edit size (`+inserted −deleted` chars), encoding (UTF-8 / UTF-8 BOM / UTF-16 LE/BE, detected from the BOM), line endings (LF/CRLF/mixed), dirty marker, mode, and context-sensitive key hints.

## Syntax highlighting

Use **Roslyn's syntax tree** (already a dependency), not csharp-ls. csharp-ls would mean an external process, the LSP protocol and project loading, which is a lot of complexity for coloring a snippet.

- Walk `root.DescendantTokens(descendIntoTrivia: true)` over the visible span and classify: keywords (`SyntaxFacts.IsKeywordKind` / contextual keywords), string/char literals, numeric literals, comments (trivia), preprocessor directives, punctuation and operators, identifiers. Heuristic: an identifier that is the name of a type declaration, follows `new`, or sits in a type-syntax position gets the type color.
- Cache the classification per file. Recompute only after edits (re-parse the edited text; Roslyn's incremental `WithChangedText` keeps it fast).
- Optional later: `Microsoft.CodeAnalysis.CSharp.Workspaces` + `Classifier.GetClassifiedSpansAsync` with an `AdhocWorkspace` for richer classification. Not needed for v1.
- Current string: highlighted background over its span. Committed strings: the snippet (or at least the string span) is rendered dimmed/gray, as the design asks.
- *Done in milestone 4:* `Tui/Highlighter.cs` sorts tokens into Keyword, ControlKeyword, Type, Method, String, Number, Comment and Preprocessor. Comments, doc comments, `#if`-disabled code and directives are classified after tokens, so they take priority. Results are cached per `SourceDocument`. A committed string grays out the whole snippet (no syntax colors), shows its span struck through on a gray background, and adds `✓ committed` to the header. `Session.MarkCommitted` is the hook that milestone 6 will call.

## Modes and keys (state machine)

```
            1                        2
 Mode0 ─────────────▶ Scroll ─────────────────▶ Edit
 (wait) ◀──── Esc ─── (1)    ◀── Esc (no diff) ─ (2)
                       ▲                          │ Esc with diff
                       │                          ▼
                       └── Esc (discard) ── Prompt "Unsaved changes, Esc discards, Enter stays editing"
                                                  │ Enter → back to Edit
 Edit ── Ctrl+S ──▶ Prompt "Commit changes? Enter commits, Esc returns"
                      Enter → write file, update set, gray out, return to Scroll
                      Esc   → back to Edit with the buffer unchanged
```

- **Mode 0 (waiting)**: shows the header and a help panel. `1` → Scroll, `2` → Edit on the current string, `q` / Ctrl+C → quit (with confirmation if anything is unsaved).
- **Scroll (1)**:
  - Up/Down: move the snippet 1 line. PgUp/PgDn: move it 6 lines.
  - **Next/previous string**: Right/Left arrows (decided). PgUp/PgDn only scroll.
  - Home: re-center on the current string. `2`: enter Edit. Esc: Mode 0.
  - When moving to a string, the snippet centers its first line.
- **Edit (2)**:
  - Cursor starts at the first character inside the string's quotes. Printable keys insert, Backspace/Delete remove, Left/Right/Home/End move the cursor, Enter inserts a newline (preserving the file's line ending).
  - Up/Down move the cursor by line, PgUp/PgDn move it by 6 lines. The snippet follows the cursor (this matches "continue to allow shifting the snippet").
  - Ctrl+S: commit prompt. Esc: back to Scroll if the diff is empty, otherwise the unsaved-changes prompt. A second Esc discards the changes and returns to **Scroll** (decided).
  - **Only Esc leaves edit mode** (decided). Digits, including `1` and `2`, are inserted as text.

## Editing and patches

- `EditSession` holds the original `SourceText`, the current `SourceText`, the cursor offset and a list of `TextChange` records (the "patch"). Each keystroke appends or merges a change, and the current text is `original.WithChanges(...)`. Edit size = sum of inserted and deleted characters. The diff is empty when the current text equals the original (compared by content, so typing then deleting goes back to "no diff").
- Editing is not restricted to the string. Context around it can be edited too (as the design says), within the file loaded into the session.
- **Commit**:
  1. Check that the file on disk is unchanged since it was loaded (compare length + hash). If it changed, refuse and offer to reload.
  2. Write the new text with the original encoding, BOM and line-ending style (`FileWriter`), atomically through a temp file.
  3. Re-parse the file, then re-scan it and replace that file's occurrences in the list. Every later span in the file has shifted, so the old ones are stale. Re-point the current index to the occurrence at the edited location.
  4. Find the new literal at the mapped original span start to get `newValue`. If the literal is gone (deleted), only the old value is removed from the set.
  5. Update the committed index (move the snippet from `(file, oldValue)` to `(file, newValue)`, see "Committed index semantics") → save `.searches`. Mark the occurrence as committed so it renders gray for the rest of the session.
  6. Optional: warn if the edit introduced new parse diagnostics (`tree.GetDiagnostics()`), so an unbalanced quote does not get saved silently. This should be a warning in the prompt, not a block.
- *As built in milestone 6 (`Editing/Committer.cs`):*
  - The file is re-read after writing (giving a fresh content hash) and re-scanned. Its list entries are rebuilt: strings that were listed before are found again at their position shifted by the patch, and keep their committed mark. Every string inside the edited region is listed, so a newly typed string appears, uncommitted. Strings outside the region that `.searches` hid at startup stay hidden.
  - The edited string is the literal at `EditSession.StringSpan` (or the first one overlapping it). If it was deleted, only its old snippet is removed.
  - Snippets of strings committed earlier this session that this edit moved (a neighboring line changed) are rewritten with the new context, so they stay hidden on the next run. Strings hidden at startup whose neighbors change are not rewritten, so they may come back once. That errs on the side of review.
  - Session committed marks are tracked by occurrence reference, not by index, so they survive re-indexing.

## Testing

xUnit project `src/tests/StringSearch.Tests` that references the app project. Note: the `Tests?` filter means the app will ignore its own tests when run on this repo, which is intended.

- `StringScanner`: in-memory sources for every literal syntax (regular, `@`, raw, `u8`), interpolated strings counted once, correct line/col and spans.
- `PathFilter`: skipped: `tests/`, `Tests/`, `UnitTests/`, `FooTests.cs`, `ParserTest.cs`, `MyApp.Tests/`, `foo_test.cs`, `TestHelpers/`. Kept: `Latest.cs`, `Contest/`, `Attestation.cs`, `Testing.cs`, `testimonials/`. Also a relative vs absolute root, and `bin`/`obj`.
- `SearchStore`: round trip with newlines/quotes/unicode, a missing file, a corrupt file, atomic save.
- Snippet matching: one of two identical values in a file committed (only that one is hidden), a committed string moved by inserted lines above it (still hidden), its context changed (shown again), duplicated identical lines (paired one-to-one in file order), and a re-edit of an already-committed string (old snippet removed).
- `EditSession`: insert/delete/merge, empty-diff detection after undoing by hand, edit size.
- `FileWriter`: BOM and CRLF preserved.
- `PositionBar`: exact output for 1/N, N/N and the sub-cell steps.
- `Controller`: table-driven tests of every transition in the state diagram above, including both prompts.
- Manual smoke test against a sample repo (a `samples/` folder with a few `.cs` files, excluded from the filter tests).

## Milestones

Status (2026-11-14): all seven milestones are done. Commits: 1–2 `99e05ca`, 3 `6964795`, 4 `30532b7`, 5 `575ba05`, 6 `762187f`, 7 in the polish commit (see log).


Each milestone builds, passes tests and is committed on its own.

1. **Skeleton**: folder layout, xUnit project, `.sln`/`.slnx`, gitignore check. Move the existing scanning into `StringScanner`.
2. **Scanning + persistence**: `StringOccurrence`, interpolated-as-one, `PathFilter`, `SearchStore`. Add a non-interactive `--list` flag that prints the occurrences (useful for debugging and scripting).
3. **Terminal + Scroll mode**: `Terminal`, `Frame` diff renderer, layout, position bar, header metadata, status bar, Mode 0/Scroll transitions, navigation, plain (unhighlighted) snippet.
4. **Highlighting**: Roslyn token classifier, current-string highlight, gray rendering for committed strings.
5. **Edit mode**: `EditSession`, cursor rendering, key handling, both prompts, the full state machine.
6. **Commit pipeline**: `FileWriter`, external-change check, re-scan of the file and re-indexing, committed-index update, `.searches` save, gray-out.
7. **Polish**: resize handling, small-terminal message, startup summary (files scanned/skipped/ignored), README usage section, error messages.

## Risks to check early

- **Ctrl+S on Linux terminals** can be taken by XON/XOFF flow control (freezing output) before the app sees it. *Checked in milestone 3 (under tmux):* `stty` still reports `ixon`, but Ctrl+S reached `Console.ReadKey` as `Control+S`, so .NET turns flow control off while it reads. *Resolved in milestone 5:* a Ctrl+S pressed while the app was printing and not reading was queued as input (`S, Control`) and output did not freeze. After the first key read, .NET keeps the terminal in its own raw mode, with IXON off, for the whole session. No `stty` is needed, and it wouldn't help anyway: .NET resets terminal settings around child processes, so a child `stty` cannot change them.
- **Esc latency/ambiguity**: .NET has to tell a lone Esc apart from the start of an escape sequence. Check that Esc feels responsive and that Alt+key arrives as a modifier.
- **Wide characters / emoji** in strings break simple column math. Use a cell-width function (East Asian Width + emoji) in `Frame`. Tabs expand to 4 columns.
- **Large files**: render only the visible lines, and classify only the visible token range.

## Decisions (user, 2026-11-14)

1. **`.searches`**: `Dictionary<(RelativePath, Value), List<Snippet>>` (originally a HashSet). It tracks each committed occurrence on its own, so the same string can appear several times in one file. Occurrences are matched by surrounding-line context, not by line number.
2. **Navigation**: Left/Right move to the previous/next string. Up/Down scroll 1 line, PgUp/PgDn scroll 6 lines.
3. **Edit mode exit**: only Esc leaves edit mode. `1` inserts the digit.
4. **Discard**: returns to Scroll mode, not Mode 0.
5. **Test filter**: case-insensitive, but "test"/"tests" has to be a whole word in a path segment (see "Path filtering"). `Latest.cs`, `Contest/` and `Attestation.cs` are not skipped.
6. **Interpolated strings**: one result per interpolated string.
