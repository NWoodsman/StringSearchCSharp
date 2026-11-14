using StringSearch.Model;
using StringSearch.Scanning;

namespace StringSearch.App;

/// <summary>The data a UI session works on: the strings to review plus scan statistics.</summary>
public sealed class Session
{
    private readonly List<StringOccurrence> _items;
    private (int Position, int Count)[] _filePositions = [];

    // By reference: two identical occurrences (same file, span and value) can't coexist, but
    // records compare by value and an item replaced after a commit must not inherit the mark.
    private readonly HashSet<StringOccurrence> _committed = new(ReferenceEqualityComparer.Instance);

    public Session(IReadOnlyList<StringOccurrence> items, ScanResult scan, int ignoredCount, SourceCache sources)
    {
        _items = [.. items];
        Scan = scan;
        IgnoredCount = ignoredCount;
        Sources = sources;
        ComputeFilePositions();
    }

    /// <summary>The strings to review, in file order. Updated in place when a file is committed.</summary>
    public IReadOnlyList<StringOccurrence> Items => _items;

    public ScanResult Scan { get; }
    public int IgnoredCount { get; }
    public SourceCache Sources { get; }

    public int CommittedCount => _committed.Count;

    /// <summary>True once the string at <paramref name="index"/> was committed this session.</summary>
    public bool IsCommitted(int index) => IsCommitted(_items[index]);

    public bool IsCommitted(StringOccurrence occurrence) => _committed.Contains(occurrence);

    public void MarkCommitted(int index) => _committed.Add(_items[index]);

    /// <summary>1-based position of item <paramref name="index"/> among the strings in its file.</summary>
    public (int Position, int Count) FilePosition(int index) => _filePositions[index];

    /// <summary>Index range of <paramref name="filePath"/>'s items (they are contiguous).</summary>
    public (int Start, int Count) FileRange(string filePath)
    {
        int start = _items.FindIndex(o => o.FilePath == filePath);
        if (start < 0)
            return (0, 0);
        int end = start;
        while (end < _items.Count && _items[end].FilePath == filePath)
            end++;
        return (start, end - start);
    }

    /// <summary>
    /// Replaces the items of <paramref name="filePath"/> with <paramref name="replacement"/>
    /// (after the file was rewritten). Each entry says whether it is committed this session.
    /// </summary>
    public void ReplaceFile(string filePath, IReadOnlyList<(StringOccurrence Occurrence, bool Committed)> replacement)
    {
        var (start, count) = FileRange(filePath);
        if (count == 0)
        {
            // The file had no visible strings left; keep the list sorted by path.
            start = _items.FindIndex(o => string.CompareOrdinal(o.RelativePath, replacement.FirstOrDefault().Occurrence?.RelativePath) > 0);
            if (start < 0)
                start = _items.Count;
        }

        foreach (var old in _items.GetRange(start, count))
            _committed.Remove(old);

        _items.RemoveRange(start, count);
        _items.InsertRange(start, replacement.Select(r => r.Occurrence));
        foreach (var (occurrence, committed) in replacement)
        {
            if (committed)
                _committed.Add(occurrence);
        }
        ComputeFilePositions();
    }

    private void ComputeFilePositions()
    {
        _filePositions = new (int, int)[_items.Count];
        int start = 0;
        for (int i = 1; i <= _items.Count; i++)
        {
            if (i == _items.Count || _items[i].FilePath != _items[start].FilePath)
            {
                for (int j = start; j < i; j++)
                    _filePositions[j] = (j - start + 1, i - start);
                start = i;
            }
        }
    }
}
