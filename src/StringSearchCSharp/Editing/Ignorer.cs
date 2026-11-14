using StringSearch.App;
using StringSearch.Model;
using StringSearch.Persistence;

namespace StringSearch.Editing;

/// <summary>Outcome of an ignore toggle.</summary>
/// <param name="Ignored">True when the string was just ignored (false: restored, refused or failed).</param>
/// <param name="Message">Status-bar message.</param>
public sealed record IgnoreResult(bool Ignored, string Message);

/// <summary>Toggles a listed string between live and ignored, recording it in <c>.searches</c>.</summary>
public static class Ignorer
{
    /// <summary>On a save failure the toggle is undone.</summary>
    public static IgnoreResult Toggle(Session session, SearchStore store, int index, DateTime? nowUtc = null)
    {
        var occurrence = session.Items[index];
        var status = session.Status(index);

        if (status == ItemStatus.Committed)
            return new IgnoreResult(false, "Already committed; it won't appear in future runs");

        bool ignore = status == ItemStatus.Live;
        if (ignore)
            store.Add(occurrence.Key, new Snippet(occurrence.Context, nowUtc ?? DateTime.UtcNow, SnippetKind.Ignored));
        else
            store.Remove(occurrence.Key, occurrence.Context, SnippetKind.Ignored);

        try
        {
            store.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (ignore)
                store.Remove(occurrence.Key, occurrence.Context, SnippetKind.Ignored);
            else
                store.Add(occurrence.Key, new Snippet(occurrence.Context, nowUtc ?? DateTime.UtcNow, SnippetKind.Ignored));
            return new IgnoreResult(false, $"Could not save {SearchStore.FileName}: {ex.Message}");
        }

        session.SetStatus(index, ignore ? ItemStatus.Ignored : ItemStatus.Live);
        return ignore
            ? new IgnoreResult(true, $"Ignored {Display.Quote(occurrence.Value, 40)}")
            : new IgnoreResult(false, $"Restored {Display.Quote(occurrence.Value, 40)}");
    }
}
