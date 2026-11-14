namespace StringSearch.Model;

/// <summary>One committed occurrence of a string, as stored in <c>.searches</c>.</summary>
public sealed record Snippet(SnippetContext Context, DateTime CommittedUtc);
