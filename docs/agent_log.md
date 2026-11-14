# Agent Log


2026-11-14 

Agents working on the project are to append a daily log entry of changes made as part of the working session. Markdown format is acceptable. 

Today's log entry is the first entry, and therefore an introduction to the log. Agents should append any changes made today below this paragraph. 

If today's date already exists in the log, read the existing text to determine if the text is instructions for the current agent (you) or a log entry of current work. 

Today's initial task is to read design.md and create a plan to implement the app design.


### Log: initial planning session (Claude Opus 5.5)

- Read `docs/design.md` (including the uncommitted revisions) and the existing `Program.cs` / `StringSearch.csproj`.
- Created `docs/plan.md`. It covers the architecture (Model / Scanning / Persistence / Editing / Tui / App layers), the data model, the `.searches` JSON format, the hand-written ANSI renderer, the braille position bar, Roslyn-based syntax highlighting (no csharp-ls), the modal state machine, the edit/commit pipeline, testing, 7 milestones, risks (e.g. Ctrl+S vs XON/XOFF) and 6 open questions for the user.
- No code changes yet.
- The user answered all 6 open questions and `docs/plan.md` was updated (see its "Decisions" section). The `.searches` key is now (file, value). Left/Right move between strings. Only Esc leaves edit mode. Discarding returns to Scroll mode. The `tests?` filter is case-insensitive. Each interpolated string is one result.
- Second round of changes to `docs/plan.md`:
  - The `.searches` HashSet was replaced with `Dictionary<(RelativePath, Value), List<Snippet>>`. A snippet stores the trimmed line holding the string, the lines before and after it, and the line number for breaking ties. That way only the committed copies are hidden when a file repeats the same string, and the match survives line shifts.
  - The test filter is case-insensitive, but "test(s)" must be a whole word in a path segment. A small throwaway program checked the regex against 20 sample names (12 skipped, 8 kept).

### Log: milestone 1, project skeleton (Claude Opus 5.5)

- Added the solution `StringSearch.slnx` at the repo root, containing the app and the new test project.
- Added the xUnit test project `src/tests/StringSearch.Tests` (from the `dotnet new xunit` template), which references the app. The template's `UnitTest1.cs` was removed.
- Checked `.gitignore`: it already covers `bin/` and `obj/`, so no change was needed.
- Moved the scanning code out of `Program.cs`:
  - `Model/StringOccurrence.cs`: `StringOccurrence` record plus the `StringKind` enum (Regular / Verbatim / Raw / Interpolated / Utf8).
  - `Scanning/StringScanner.cs`: `ScanSource` (one file's text) and `ScanDirectory` (returns a `ScanResult` with the occurrences, the scanned files and the skipped files).
  - `Scanning/PathFilter.cs`: skips `bin/` and `obj/`. It now checks directory names in the relative path instead of looking for `/bin/` substrings in the absolute path.
  - `Program.cs` now only checks the arguments and prints the results in the old format.
- Behavior is the same as before (checked against a sample tree), with one change: results are sorted by relative path and then by position in the file. Before, all plain literals came first and the interpolated parts after them. Interpolated strings still yield one result per text part. Turning them into one result each, plus the test-folder filter and `.searches`, is milestone 2.
- Tests: 12 passing (`dotnet test StringSearch.slnx`), covering the literal kinds, line/column, interpolated ordering, and directory scanning with bin/obj skipped.
- Nothing committed.

### Log: snippet format and string kind removed (Claude Opus 5.5)

- `docs/plan.md`: `.searches` snippets no longer store line numbers. Line numbers are calculated from the source when the file is scanned on load, and are only used for display. Occurrences with identical context are matched to stored snippets in file order, one-to-one.
- `docs/plan.md`: an entry is one exact `(file, value)` pair, compared character for character (ordinal). A value that differs by even one character is a new entry. Exact matches in the same file are separate snippets in one entry.
- Removed string kind (regular/verbatim/raw/utf8) from the plan and the code: dropped the `StringKind` enum, `StringOccurrence.Kind` and the classification in `StringScanner`. As a result, `Program.cs` no longer prints the `(Interpolated)` label. Tests were updated, and 12 still pass.

### Log: milestone 2, scanning and persistence (Claude Opus 5.5)

- `Model/`: added `SearchKey` (record struct of relative path and value, compared ordinally), `SnippetContext` (the line(s) holding the literal plus the nearest non-blank line before and after, all trimmed; built from Roslyn's `SourceText` line map; `Matches` = same text and at least one same neighbor), and `Snippet` (context plus commit time). `StringOccurrence` now carries its `Context` and a `Key`.
- `Scanning/StringScanner.cs`: each interpolated string is now one result. Holes are kept as written (`{n,3:F1}`) and escaped braces stay doubled, which is what Roslyn's `ValueText` gives. Literals nested inside holes are separate results. Files are parsed in parallel with the original order kept. Dot-directories are now included in the walk so that `PathFilter` decides about them. `ScanResult.SkippedFiles` now records a reason (`BuildOutput` or `Test`).
- `Scanning/PathFilter.cs`: `bin`, `obj` and `.git` count as build output, plus the whole-word test regex applied to each directory name and to the file name without its extension. The regex is a `[GeneratedRegex]`.
- `Scanning/CommittedFilter.cs`: splits the found strings into visible and ignored. It pairs exact context matches first, then loose ones, in file order, and each snippet hides at most one occurrence.
- `Persistence/SearchStore.cs`: loads and saves `.searches` as JSON (`version`, `entries[file, value, snippets[before, text, after, committedUtc]]`, with no line numbers). Saving is atomic (temp file plus rename), and entries are sorted for stable diffs. A file that is corrupt, has missing or null fields, or has an unknown version is moved to `.searches.bak` and a warning is shown. `Add` and `Remove` (exact match preferred) are ready for the commit step in milestone 6.
- `Program.cs`: `StringSearch <directory> [--list]`. It prints the visible strings as `path:line:col  "escaped value"` plus a summary line. `--list` also lists the skipped files with their reasons. Bad arguments exit with code 1. Until the UI arrives in milestone 3, the program runs this way without `--list` too.
- Tests: 54 passing. They cover scanner values and context, all 29 path-filter examples, the store's round trip, JSON shape and corrupt-file handling, and the committed-filter cases from the plan.
- Manual check: on a sample tree with a hand-written `.searches`, the committed string was hidden. A corrupt file was moved to `.bak` with a warning. On this repo, `src/tests` and the `obj/` files are skipped.
- Nothing committed.

### Log: commit, then milestone 3, terminal and scroll mode (Claude Opus 5.5)

- Created branch `string-search-tui` and committed the earlier work (`9c62c05` docs, `99e05ca` milestones 1–2).
- `Tui/`:
  - `Style` (24-bit `Rgb`, attributes) and `CellWidth` (0/1/2 columns per code point: combining marks, East Asian wide, emoji).
  - `Frame`, a cell grid that handles wide glyphs.
  - `AnsiWriter`, which rewrites only changed rows; truecolor, or the 256-color cube when `COLORTERM` doesn't say truecolor.
  - `Terminal`: alternate screen, hidden cursor, no auto-wrap, Ctrl+C read as a key, size polling. Everything is restored on dispose.
  - `Layout`: position bar, location line, info line, rules, snippet pane and status bar; a minimum size of 40x10, with a message below that.
  - `PositionBar`: braille fill in eighths of a cell, `●` marker, a blue → amber → green gradient, and a padded `XX/XXX` label.
  - `SnippetView`: line-number gutter, tabs expanded to 4 columns, the current string highlighted, horizontal scrolling when the string starts beyond the pane, and `‹`/`›` markers where lines are cut.
  - `Screen` composes the frame.
- `App/`: `AppState` (immutable), `Controller` (pure state machine), `Session` (items plus per-file "string k/n in file") and `TuiApp` (the main loop).
  - Mode 0: `1` scroll, `2` shows "Edit mode is not implemented yet", `q` quits.
  - Scroll mode: ←/→ previous/next string (a message at either end), ↑/↓ ±1 line, PgUp/PgDn ±6, Home re-centers, Esc back to mode 0. Ctrl+C quits from anywhere.
  - On resize, the scroll position is re-clamped and the view re-centers if the current string went out of view.
- `Model/SourceDocument` + `SourceCache`: decodes the way `File.ReadAllText` does (so scanner spans line up), detects the encoding from the BOM and the line endings (LF/CRLF/CR/Mixed). Both are shown in the status bar.
- `Program.cs` runs the UI by default. It prints the list with `--list`, or when input or output is redirected.
- Ctrl+S check (tmux): `stty` still reports `ixon`, but Ctrl+S reaches `Console.ReadKey`. Recorded in the plan's risks; milestone 5 will add `stty -ixon`.
- Driving it in tmux found two problems, both fixed: a resize could leave the current string off-screen, and the location line was cut on the right (now the path is shortened from the left with `…`).
- Tests: 108 passing (54 new: frame, ANSI, position bar, snippet view, controller transitions, source document, session).
- Not committed yet.

### Log: commit, then milestone 4, syntax highlighting (Claude Opus 5.5)

- Committed milestone 3 as `6964795`.
- `Tui/Highlighter.cs`: syntactic classification from the Roslyn syntax tree, one `TokenClass` per character, cached per `SourceDocument` (`ConditionalWeakTable`). It covers keywords (including contextual ones such as `async`/`with` and `var`/`dynamic` used as types), control-flow keywords, types (declarations, type-only contexts via `SyntaxFacts.IsInTypeOnlyContext`, `new X()`, `typeof`, attributes, generic names), methods (declarations and invocations), strings (all literal, interpolated and raw token kinds; holes are left uncolored), numbers, comments (line, block, doc, disabled `#if` code) and preprocessor directives.
- `Theme`: syntax colors (foreground only, mid-tone), plus committed styles. `TextAttr` gained Italic (comments) and Strikethrough (committed string).
- `Session`: `MarkCommitted` / `IsCommitted` / `CommittedCount`. A committed string renders the snippet gray with the span struck through, shows `✓ committed` in the header, and adds "N committed this session" at the start of the info line. Nothing calls `MarkCommitted` yet; milestone 6 will.
- `SnippetView.Draw` takes an optional highlight style.
- Checked in tmux (decoding the escape codes): keywords, control keywords, types, methods, numbers, doc comments and interpolated-string parts are colored as expected.
- Tests: 151 passing (43 new: token classes, disabled code, cache, normal vs committed screen rendering).
- Not committed yet.

### Log: commit, then milestone 5, edit mode (Claude Opus 5.5)

- Committed milestone 4 as `30532b7`.
- Ctrl+S risk resolved (tested in tmux): with output running and no key read in progress, Ctrl+S was queued as a key and output did not freeze, because .NET keeps IXON off once it has configured the terminal. A child `stty -ixon` would not help, since .NET resets terminal settings around child processes. Recorded in the plan.
- `Editing/EditSession.cs`: an immutable edit buffer holding the original document, the current `SourceText`, the cursor, the goal column for Up/Down, the string span (kept in step with edits by `AdjustSpan`) and the raw `TextChange` history.
  - `Patch` is the minimal change against the original (common prefix and suffix removed). `Size` gives +inserted/−deleted from it, and `IsDirty` compares content, so typing and then deleting the same text is clean again.
  - Editing: insert, Backspace, Delete (CRLF and surrogate pairs count as one unit), smart Home, End, vertical moves that keep the display column, and Enter using the file's own line ending.
  - The cursor starts just inside the opening delimiter (`$`, `@`, and one or more quotes).
- `App/AppState`: new `Edit`, `Prompt` (ConfirmCommit / ConfirmDiscard / ConfirmQuit) and `Effect` (Commit) fields.
- `App/Controller`: `2` enters edit mode from mode 0 or scroll mode. In edit mode only Esc leaves, and every printable key (digits included) is typed; Ctrl/Alt chords are not.
  - Ctrl+S asks to commit when there are changes: Enter sets `Effect.Commit`, Esc returns to the buffer. Without changes it just shows a message.
  - Esc: with no changes it goes back to scroll mode; with changes it asks, then a second Esc discards and returns to scroll mode, while Enter keeps editing.
  - Ctrl+C with unsaved changes asks; a second Ctrl+C quits.
  - Arrows, PgUp/PgDn and resizing keep the cursor in view.
  - The controller now takes a `SourceDocument` provider instead of a line-count function.
- Rendering:
  - `Frame.Cursor` puts the real terminal cursor (a blinking bar) in the right cell. `AnsiWriter` emits the cursor only when something changed, and `Terminal` resets the cursor shape on exit.
  - `SnippetView` returns the cursor's cell and scrolls sideways to follow the cursor.
  - `Screen` in edit mode shows the edited text with live highlighting, `● modified`, a status bar with `Ln/Col │ Δ +i −d │ encoding │ line endings │ lines`, prompts that replace the status bar, and the edit keys in the help screen.
- `TuiApp`: on `Effect.Commit` it shows "Saving is not implemented yet" and keeps the buffer. Milestone 6 replaces this with the real commit.
- Manual check in tmux: entering edit mode, typing digits, Backspace and End, both prompts, discarding, Enter splitting a line, and the Ctrl+C prompt then quit. The sample file's md5 was unchanged, so nothing is written yet.
- Tests: 199 passing (48 new: EditSession, edit-mode controller flows, cursor output, cursor in the snippet, prompt rendering).
- Not committed yet.
