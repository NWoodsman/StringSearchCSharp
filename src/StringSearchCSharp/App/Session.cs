using StringSearch.Model;
using StringSearch.Scanning;

namespace StringSearch.App;

/// <summary>The data a UI session works on: the strings to review plus scan statistics.</summary>
public sealed class Session
{
    private readonly (int Position, int Count)[] _filePositions;
    private readonly HashSet<int> _committed = [];

    public Session(IReadOnlyList<StringOccurrence> items, ScanResult scan, int ignoredCount, SourceCache sources)
    {
        Items = items;
        Scan = scan;
        IgnoredCount = ignoredCount;
        Sources = sources;

        _filePositions = new (int, int)[items.Count];
        int start = 0;
        for (int i = 1; i <= items.Count; i++)
        {
            if (i == items.Count || items[i].FilePath != items[start].FilePath)
            {
                for (int j = start; j < i; j++)
                    _filePositions[j] = (j - start + 1, i - start);
                start = i;
            }
        }
    }

    public IReadOnlyList<StringOccurrence> Items { get; }
    public ScanResult Scan { get; }
    public int IgnoredCount { get; }
    public SourceCache Sources { get; }

    public int CommittedCount => _committed.Count;

    /// <summary>True once the string at <paramref name="index"/> was committed this session (milestone 6).</summary>
    public bool IsCommitted(int index) => _committed.Contains(index);

    public void MarkCommitted(int index) => _committed.Add(index);

    /// <summary>1-based position of item <paramref name="index"/> among the strings in its file.</summary>
    public (int Position, int Count) FilePosition(int index) => _filePositions[index];
}
