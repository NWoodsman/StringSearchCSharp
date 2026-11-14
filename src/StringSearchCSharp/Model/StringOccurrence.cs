using Microsoft.CodeAnalysis.Text;

namespace StringSearch.Model;

/// <summary>One string literal found in a source file.</summary>
/// <param name="FilePath">Absolute path of the source file.</param>
/// <param name="RelativePath">Path relative to the target directory, always with '/' separators.</param>
/// <param name="Span">Span of the literal in the file text.</param>
/// <param name="Line">1-based line of the literal's start.</param>
/// <param name="Column">1-based column of the literal's start.</param>
/// <param name="RawText">Source text of the literal, including quotes and prefixes.</param>
/// <param name="Value">
/// The literal's value with escape sequences resolved. For interpolated strings, holes are
/// kept as their source text and escaped braces stay doubled, e.g. <c>Hello {name} {{literal}}</c>.
/// </param>
/// <param name="Context">Surrounding source lines, used to recognize committed strings on later runs.</param>
public sealed record StringOccurrence(
    string FilePath,
    string RelativePath,
    TextSpan Span,
    int Line,
    int Column,
    string RawText,
    string Value,
    SnippetContext Context)
{
    public SearchKey Key => new(RelativePath, Value);
}
