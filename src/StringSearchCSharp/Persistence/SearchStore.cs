using System.Text.Encodings.Web;
using System.Text.Json;
using StringSearch.Model;

namespace StringSearch.Persistence;

/// <summary>
/// The <c>.searches</c> file in the target directory: the strings the user has committed,
/// keyed by (file, value), with one <see cref="Snippet"/> per committed occurrence.
/// </summary>
public sealed class SearchStore
{
    public const string FileName = ".searches";
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        // Local, human-readable file: keep non-ASCII and quotes unescaped where JSON allows it.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Dictionary<SearchKey, List<Snippet>> _entries;

    private SearchStore(string filePath, Dictionary<SearchKey, List<Snippet>> entries, string? loadWarning)
    {
        FilePath = filePath;
        _entries = entries;
        LoadWarning = loadWarning;
    }

    public string FilePath { get; }

    /// <summary>Set when the file existed but could not be read; it was moved to <c>.searches.bak</c>. Short enough for the status bar.</summary>
    public string? LoadWarning { get; }

    /// <summary>The underlying reason for <see cref="LoadWarning"/> (parser message etc.).</summary>
    public string? LoadWarningDetail { get; private init; }

    public IReadOnlyDictionary<SearchKey, List<Snippet>> Entries => _entries;

    public int SnippetCount => _entries.Values.Sum(s => s.Count);

    /// <summary>
    /// Loads <c>.searches</c> from <paramref name="rootDirectory"/>. A missing file gives an empty
    /// store. An unreadable or newer-version file is moved to <c>.searches.bak</c> (so it is never
    /// overwritten) and an empty store with <see cref="LoadWarning"/> set is returned.
    /// </summary>
    public static SearchStore Load(string rootDirectory)
    {
        var path = Path.Combine(rootDirectory, FileName);
        if (!File.Exists(path))
            return new SearchStore(path, [], null);

        try
        {
            using var stream = File.OpenRead(path);
            var dto = JsonSerializer.Deserialize<FileDto>(stream, JsonOptions)
                ?? throw new JsonException("File is empty.");
            if (dto.Version != CurrentVersion)
                throw new JsonException($"Unsupported version {dto.Version}.");

            var entries = new Dictionary<SearchKey, List<Snippet>>();
            foreach (var entry in dto.Entries)
            {
                var key = new SearchKey(entry.File, entry.Value);
                if (!entries.TryGetValue(key, out var list))
                    entries[key] = list = [];
                list.AddRange(entry.Snippets.Select(s =>
                    new Snippet(new SnippetContext(s.Before, s.Text, s.After), s.CommittedUtc)));
            }
            return new SearchStore(path, entries, null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            var backup = path + ".bak";
            File.Move(path, backup, overwrite: true);
            return new SearchStore(path, [],
                $"Could not read {FileName}; moved it to {Path.GetFileName(backup)} and started fresh")
            {
                LoadWarningDetail = ex.Message,
            };
        }
    }

    public void Add(SearchKey key, Snippet snippet)
    {
        if (!_entries.TryGetValue(key, out var list))
            _entries[key] = list = [];
        list.Add(snippet);
    }

    /// <summary>
    /// Removes one snippet under <paramref name="key"/> that matches <paramref name="context"/>,
    /// preferring an exact context match. Drops the key when its last snippet is removed.
    /// </summary>
    public bool Remove(SearchKey key, SnippetContext context)
    {
        if (!_entries.TryGetValue(key, out var list))
            return false;

        int index = list.FindIndex(s => s.Context == context);
        if (index < 0)
            index = list.FindIndex(s => s.Context.Matches(context));
        if (index < 0)
            return false;

        list.RemoveAt(index);
        if (list.Count == 0)
            _entries.Remove(key);
        return true;
    }

    /// <summary>Writes the file atomically (temp file + rename). Entries are sorted for stable diffs.</summary>
    public void Save()
    {
        var dto = new FileDto(
            CurrentVersion,
            _entries
                .OrderBy(e => e.Key.RelativePath, StringComparer.Ordinal)
                .ThenBy(e => e.Key.Value, StringComparer.Ordinal)
                .Select(e => new EntryDto(
                    e.Key.RelativePath,
                    e.Key.Value,
                    e.Value.Select(s => new SnippetDto(
                        s.Context.Before, s.Context.Text, s.Context.After, s.CommittedUtc)).ToList()))
                .ToList());

        var temp = FilePath + ".tmp";
        using (var stream = File.Create(temp))
        {
            JsonSerializer.Serialize(stream, dto, JsonOptions);
        }
        File.Move(temp, FilePath, overwrite: true);
    }

    private sealed record FileDto(int Version, List<EntryDto> Entries);

    private sealed record EntryDto(string File, string Value, List<SnippetDto> Snippets);

    private sealed record SnippetDto(string Before, string Text, string After, DateTime CommittedUtc);
}
