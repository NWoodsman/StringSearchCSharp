using StringSearch.Model;

namespace StringSearch.Scanning;

/// <summary>Found strings split into those still to review and those already committed.</summary>
public sealed record FilterResult(
    IReadOnlyList<StringOccurrence> Visible,
    IReadOnlyList<StringOccurrence> Ignored);

/// <summary>Hides found strings that match a committed snippet from <c>.searches</c>.</summary>
public static class CommittedFilter
{
    /// <summary>
    /// An occurrence is hidden when a snippet under the same (file, value) key matches its
    /// context. Each snippet hides at most one occurrence. Exact context matches are paired
    /// first, then loose matches (<see cref="SnippetContext.Matches"/>); within each pass,
    /// occurrences are taken in file order, so identical duplicates are hidden first-come.
    /// </summary>
    /// <param name="occurrences">Found strings in file order.</param>
    public static FilterResult Apply(
        IReadOnlyList<StringOccurrence> occurrences,
        IReadOnlyDictionary<SearchKey, List<Snippet>> committed)
    {
        var used = new Dictionary<SearchKey, bool[]>();
        var hidden = new bool[occurrences.Count];

        foreach (var exact in new[] { true, false })
        {
            for (int i = 0; i < occurrences.Count; i++)
            {
                if (hidden[i])
                    continue;

                var occurrence = occurrences[i];
                if (!committed.TryGetValue(occurrence.Key, out var snippets))
                    continue;

                if (!used.TryGetValue(occurrence.Key, out var taken))
                    used[occurrence.Key] = taken = new bool[snippets.Count];

                for (int s = 0; s < snippets.Count; s++)
                {
                    var context = snippets[s].Context;
                    bool match = exact ? context == occurrence.Context : context.Matches(occurrence.Context);
                    if (!taken[s] && match)
                    {
                        taken[s] = true;
                        hidden[i] = true;
                        break;
                    }
                }
            }
        }

        var visible = new List<StringOccurrence>();
        var ignored = new List<StringOccurrence>();
        for (int i = 0; i < occurrences.Count; i++)
            (hidden[i] ? ignored : visible).Add(occurrences[i]);

        return new FilterResult(visible, ignored);
    }
}
