using StringSearch.Model;
using StringSearch.Scanning;

namespace StringSearch.App;

/// <summary>What the user did with a listed string this session.</summary>
public enum ItemStatus
{
    /// <summary>Still to review.</summary>
    Live,
    /// <summary>Edited and committed; recorded in .searches.</summary>
    Committed,
    /// <summary>Marked as not needing review (I); recorded in .searches until restored.</summary>
    Ignored,
}

/// <summary>The data a UI session works on: the strings to review plus scan statistics.</summary>
public sealed class Session
{
    private readonly List<StringOccurrence> _items;
    private (int Position, int Count)[] _filePositions = [];

    // By reference: records compare by value, and an item replaced after a commit must not
    // inherit a mark by accident. Items not in the dictionary are live.
    private readonly Dictionary<StringOccurrence, ItemStatus> _status = new(ReferenceEqualityComparer.Instance);

    /// <param name="hiddenCount">Strings hidden at startup because .searches lists them.</param>
    public Session(IReadOnlyList<StringOccurrence> items, ScanResult scan, int hiddenCount, SourceCache sources)
    {
        _items = [.. items];
        Scan = scan;
        HiddenCount = hiddenCount;
        Sources = sources;
        ComputeFilePositions();
    }

    /// <summary>The strings to review, in file order. Updated in place when a file is committed.</summary>
    public IReadOnlyList<StringOccurrence> Items => _items;

    public ScanResult Scan { get; }

    /// <summary>Strings hidden at startup because .searches lists them.</summary>
    public int HiddenCount { get; }

    public SourceCache Sources { get; }

    public int CommittedCount => _status.Values.Count(s => s == ItemStatus.Committed);

    /// <summary>Strings ignored this session (and not restored).</summary>
    public int IgnoredCount => _status.Values.Count(s => s == ItemStatus.Ignored);

    public ItemStatus Status(int index) => Status(_items[index]);

    public ItemStatus Status(StringOccurrence occurrence) =>
        _status.TryGetValue(occurrence, out var status) ? status : ItemStatus.Live;

    public void SetStatus(int index, ItemStatus status) => SetStatus(_items[index], status);

    private void SetStatus(StringOccurrence occurrence, ItemStatus status)
    {
        if (status == ItemStatus.Live)
            _status.Remove(occurrence);
        else
            _status[occurrence] = status;
    }

    /// <summary>True once the string at <paramref name="index"/> was committed this session.</summary>
    public bool IsCommitted(int index) => Status(index) == ItemStatus.Committed;

    public bool IsCommitted(StringOccurrence occurrence) => Status(occurrence) == ItemStatus.Committed;

    public bool IsIgnored(int index) => Status(index) == ItemStatus.Ignored;

    public void MarkCommitted(int index) => SetStatus(index, ItemStatus.Committed);

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
    /// (after the file was rewritten), each with its status.
    /// </summary>
    public void ReplaceFile(string filePath, IReadOnlyList<(StringOccurrence Occurrence, ItemStatus Status)> replacement)
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
            _status.Remove(old);

        _items.RemoveRange(start, count);
        _items.InsertRange(start, replacement.Select(r => r.Occurrence));
        foreach (var (occurrence, status) in replacement)
            SetStatus(occurrence, status);
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
