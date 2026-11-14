namespace StringSearch.Model;

/// <summary>Why a string was recorded in <c>.searches</c>. Either way it is hidden on later runs.</summary>
public enum SnippetKind
{
    /// <summary>The user edited and committed the string.</summary>
    Committed,
    /// <summary>The user marked the string as not needing review (I in scroll mode).</summary>
    Ignored,
}

/// <summary>One recorded occurrence of a string, as stored in <c>.searches</c>.</summary>
/// <param name="CommittedUtc">When it was recorded (committed or ignored).</param>
public sealed record Snippet(SnippetContext Context, DateTime CommittedUtc, SnippetKind Kind = SnippetKind.Committed);
