# StringSearchCSharp
Interactive TUI for editing strings in a dotnet project.

StringSearch finds every string literal in the C# files of a directory and lets you step
through them in a full-screen terminal UI, edit them in place, and commit the edits back to
the source files. Committed strings are remembered in a `.searches` file, so the next run only
shows what is left to review.

## Build and run

Requires the .NET 10 SDK.

```sh
dotnet build StringSearch.slnx
dotnet run --project src/StringSearchCSharp -- <directory>
```

```
Usage: StringSearch <directory> [--list]

  --list       print the strings instead of opening the UI
  -h, --help   show help
```

When output is not a terminal (piped or redirected), the list is printed instead of opening
the UI.

## Screen

```
⣿⣿⣿⣿⣿⣿⣿⡆●                                           42/318   position bar
src/Services/Mailer.cs:118:24   string 3/7 in file      SCROLL   location, mode
2 committed this session · 57 ignored via .searches · …          statistics
─────────────────────────────────────────────────────────────
 117 │     _log.Info("Preparing");
 118 │     _log.Info("Sending message to {0}", m.To);             snippet, syntax-highlighted,
 119 │     await _client.SendAsync(m);                              current string highlighted
─────────────────────────────────────────────────────────────
 Ln 118, Col 24 │ UTF-8 │ CRLF │ 240 lines        key hints      status bar
```

The braille bar grows as you move through the results and shifts from blue to green near
the end. Committed strings are shown grayed out and struck through.

## Keys

| Mode | Key | Action |
|------|-----|--------|
| waiting (0) | `1` | scroll mode |
| | `2` | edit mode on the current string |
| | `q` | quit |
| scroll (1) | `←` `→` | previous / next string |
| | `↑` `↓` | scroll one line |
| | `PgUp` `PgDn` | scroll six lines |
| | `Home` | re-center on the current string |
| | `2` | edit mode |
| | `Esc` | back to waiting mode |
| edit (2) | typing | inserts text, digits included; `Enter`, `Tab`, `Backspace`, `Delete` |
| | arrows, `Home`, `End`, `PgUp`, `PgDn` | move the cursor |
| | `Ctrl+S` | commit: `Enter` writes the file, `Esc` returns to editing |
| | `Esc` | leave; with unsaved changes asks: `Esc` discards, `Enter` keeps editing |
| anywhere | `Ctrl+C` | quit (asks first if there are unsaved changes) |

Only `Esc` leaves edit mode. You can edit the string and the code around it. The commit
prompt warns if the edit adds syntax errors.

## What gets scanned

- Every `*.cs` file under the directory, except files in `bin/`, `obj/` and `.git/`.
- Test code is skipped: a folder or file name that is `test`/`tests` (any case), ends in
  `Test`/`Tests` (`UnitTests/`, `FooTests.cs`, `MyApp.Tests/`, `foo_test.cs`), or starts with
  `test(s)` followed by a capital letter or `.` `_` `-` (`TestHelpers/`). `Latest.cs`,
  `Contest/` and `Testing.cs` are not skipped.
- Regular, verbatim (`@"…"`), raw (`"""…"""`), UTF-8 (`"…"u8`) and interpolated strings. An
  interpolated string is one result; a string inside one of its holes is its own result.

`--list` shows the skipped files and why.

## Committing and `.searches`

Committing an edit:

1. checks the file has not changed on disk since you started editing (otherwise nothing is
   written);
2. writes the file with its original encoding, BOM, line endings and permissions;
3. re-scans the file and marks the edited string as committed;
4. records the string in `<directory>/.searches`.

`.searches` is JSON. Each entry is a file and an exact string value; each committed copy of
that string is a snippet holding its own line and the nearest non-blank lines above and below:

```json
{
  "version": 1,
  "entries": [
    {
      "file": "src/Foo.cs",
      "value": "Hello",
      "snippets": [
        { "before": "var name = GetName();", "text": "Console.WriteLine(\"Hello\");",
          "after": "return;", "committedUtc": "2026-11-14T10:00:00Z" }
      ]
    }
  ]
}
```

On the next run a string is skipped when its file and value match an entry, and its own line
and at least one neighboring line match a snippet. Line numbers are not stored, so edits
elsewhere in the file do not matter. If the code around a committed string changes a lot, the
string shows up again for review.

Delete `.searches` to review every string again. If the file is unreadable, it is moved to
`.searches.bak` and the app starts fresh.

## Tests

```sh
dotnet test StringSearch.slnx
```

## Project layout

```
src/StringSearchCSharp/
  Model/        StringOccurrence, SearchKey, Snippet(Context), SourceDocument, Display
  Scanning/     StringScanner (Roslyn), PathFilter, CommittedFilter
  Persistence/  SearchStore (.searches)
  Editing/      EditSession, Committer, FileWriter
  Tui/          Terminal, Frame, AnsiWriter, Layout, PositionBar, SnippetView, Highlighter, Screen, Theme
  App/          AppState, Controller (modal state machine), Session, TuiApp
src/tests/StringSearch.Tests/   xUnit tests
docs/           design.md, plan.md, agent_log.md
```
