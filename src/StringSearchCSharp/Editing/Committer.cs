using Microsoft.CodeAnalysis.Text;
using StringSearch.App;
using StringSearch.Model;
using StringSearch.Persistence;
using StringSearch.Scanning;

namespace StringSearch.Editing;

/// <summary>Outcome of a commit. On failure nothing was written and the edit stays open.</summary>
/// <param name="Success">True when the file was written.</param>
/// <param name="Message">Shown in the status bar.</param>
/// <param name="Index">Index of the edited string after re-indexing (unchanged on failure).</param>
public sealed record CommitResult(bool Success, string Message, int Index);

/// <summary>
/// Carries out a confirmed commit: write the file, re-scan it, update the session's list and
/// the committed snippets in <c>.searches</c>.
/// </summary>
public static class Committer
{
    public static CommitResult Commit(Session session, SearchStore store, int index, EditSession edit, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var old = session.Items[index];
        var path = old.FilePath;

        if (!edit.Original.IsUnchangedOnDisk())
            return new CommitResult(false,
                $"{old.RelativePath} changed on disk since editing started; not saved. Esc twice discards your edit.", index);

        // 1. Write the file.
        var newText = edit.Text.ToString();
        try
        {
            FileWriter.Write(path, newText, edit.Original.EncodingName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CommitResult(false, $"Could not write {old.RelativePath}: {ex.Message}", index);
        }

        var document = SourceDocument.Load(path);
        session.Sources.Set(document);

        // 2. Re-scan the file and work out which of its strings stay in the list.
        var (start, count) = session.FileRange(path);
        var oldItems = Enumerable.Range(start, count).Select(i => session.Items[i]).ToList();
        var rescanned = StringScanner.ScanSource(newText, path, old.RelativePath);
        var patch = edit.Patch;
        var edited = FindEdited(rescanned, edit.StringSpan);
        var replacement = Reconcile(session, oldItems, rescanned, patch, edited);

        // 3. Update the recorded snippets.
        if (session.IsCommitted(old))
            store.Remove(old.Key, old.Context, SnippetKind.Committed);
        foreach (var item in oldItems)
        {
            // Other committed/ignored strings inside the edited region: the edit rewrote them,
            // so they are live again and their snippets go.
            if (!ReferenceEquals(item, old) && Kind(session.Status(item)) is { } kind && MapStart(item.Span, patch) is null)
                store.Remove(item.Key, item.Context, kind);
        }
        foreach (var (before, after, kind) in MovedMarked(session, oldItems, rescanned, patch))
        {
            // Committed/ignored strings whose surroundings this edit changed: keep them hidden next run.
            if (store.Remove(before.Key, before.Context, kind))
                store.Add(after.Key, new Snippet(after.Context, now, kind));
        }
        if (edited is not null)
            store.Add(edited.Key, new Snippet(edited.Context, now));

        session.ReplaceFile(path, replacement);
        int newIndex = NewIndex(session, edited, start, count: replacement.Count);

        string saveWarning = "";
        try
        {
            store.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            saveWarning = $" (but {SearchStore.FileName} could not be saved: {ex.Message})";
        }

        string message = edited is null
            ? $"Committed {old.RelativePath}; the string was removed{saveWarning}"
            : $"Committed {Display.Quote(old.Value, 30)} → {Display.Quote(edited.Value, 30)}{saveWarning}";
        return new CommitResult(true, message, newIndex);
    }

    /// <summary>The literal at the edited string's tracked span, or the first one overlapping it; null if it was deleted.</summary>
    public static StringOccurrence? FindEdited(IReadOnlyList<StringOccurrence> rescanned, TextSpan stringSpan) =>
        rescanned.FirstOrDefault(o => o.Span == stringSpan)
        ?? rescanned.FirstOrDefault(o => o.Span.OverlapsWith(stringSpan) || (stringSpan.IsEmpty && o.Span.Contains(stringSpan.Start)));

    private static SnippetKind? Kind(ItemStatus status) => status switch
    {
        ItemStatus.Committed => SnippetKind.Committed,
        ItemStatus.Ignored => SnippetKind.Ignored,
        _ => null,
    };

    /// <summary>
    /// The file's new list: strings that were listed before (found again at their position
    /// shifted by the patch, keeping their status), plus every string inside the edited region
    /// (live). Strings outside the region that were not listed (hidden by .searches) stay hidden.
    /// </summary>
    private static List<(StringOccurrence, ItemStatus)> Reconcile(Session session, List<StringOccurrence> oldItems,
        IReadOnlyList<StringOccurrence> rescanned, TextChange patch, StringOccurrence? edited)
    {
        var byNewStart = new Dictionary<int, StringOccurrence>();
        foreach (var item in oldItems)
        {
            if (MapStart(item.Span, patch) is { } mapped)
                byNewStart[mapped] = item;
        }

        var region = new TextSpan(patch.Span.Start, patch.NewText!.Length);
        var result = new List<(StringOccurrence, ItemStatus)>();
        foreach (var occurrence in rescanned)
        {
            if (ReferenceEquals(occurrence, edited))
                result.Add((occurrence, ItemStatus.Committed));
            else if (byNewStart.TryGetValue(occurrence.Span.Start, out var before))
                result.Add((occurrence, session.Status(before)));
            else if (occurrence.Span.IntersectsWith(region) || occurrence.Span.OverlapsWith(region))
                result.Add((occurrence, ItemStatus.Live));
        }
        return result;
    }

    private static IEnumerable<(StringOccurrence Before, StringOccurrence After, SnippetKind Kind)> MovedMarked(
        Session session, List<StringOccurrence> oldItems, IReadOnlyList<StringOccurrence> rescanned, TextChange patch)
    {
        foreach (var item in oldItems)
        {
            if (Kind(session.Status(item)) is not { } kind || MapStart(item.Span, patch) is not { } mapped)
                continue;
            var after = rescanned.FirstOrDefault(o => o.Span.Start == mapped);
            if (after is not null && after.Context != item.Context)
                yield return (item, after, kind);
        }
    }

    /// <summary>Start of <paramref name="span"/> after <paramref name="patch"/>, or null if the patch touches the span.</summary>
    public static int? MapStart(TextSpan span, TextChange patch)
    {
        if (span.End <= patch.Span.Start)
            return span.Start;
        if (span.Start >= patch.Span.End)
            return span.Start + patch.NewText!.Length - patch.Span.Length;
        return null;
    }

    private static int NewIndex(Session session, StringOccurrence? edited, int fileStart, int count)
    {
        if (session.Items.Count == 0)
            return 0;
        if (edited is not null)
        {
            for (int i = 0; i < session.Items.Count; i++)
            {
                if (ReferenceEquals(session.Items[i], edited))
                    return i;
            }
        }
        return Math.Clamp(fileStart, 0, session.Items.Count - 1);
    }
}
