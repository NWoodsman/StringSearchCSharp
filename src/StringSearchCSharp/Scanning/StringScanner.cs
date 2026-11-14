using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using StringSearch.Model;

namespace StringSearch.Scanning;

/// <summary>A file left out of the scan, and why. <paramref name="Detail"/> explains unreadable files.</summary>
public sealed record SkippedFile(string RelativePath, SkipReason Reason, string? Detail = null);

/// <summary>Result of scanning a directory tree.</summary>
/// <param name="Occurrences">Found strings, ordered by relative path, then position.</param>
/// <param name="ScannedFiles">Relative paths of the files that were parsed.</param>
/// <param name="SkippedFiles">Files excluded by <see cref="PathFilter"/>.</param>
public sealed record ScanResult(
    IReadOnlyList<StringOccurrence> Occurrences,
    IReadOnlyList<string> ScannedFiles,
    IReadOnlyList<SkippedFile> SkippedFiles);

/// <summary>Finds string literals in C# source using Roslyn.</summary>
public static class StringScanner
{
    /// <summary>Scans every non-excluded <c>*.cs</c> file under <paramref name="rootDirectory"/>.</summary>
    public static ScanResult ScanDirectory(string rootDirectory)
    {
        var root = Path.GetFullPath(rootDirectory);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = 0, // include dot-directories; PathFilter decides
        };

        var scanned = new List<(string Full, string Relative)>();
        var skipped = new List<SkippedFile>();

        var files = Directory.EnumerateFiles(root, "*.cs", options)
            .Select(f => (Full: f, Relative: Path.GetRelativePath(root, f).Replace('\\', '/')))
            .OrderBy(f => f.Relative, StringComparer.Ordinal);

        foreach (var file in files)
        {
            if (PathFilter.GetSkipReason(file.Relative) is { } reason)
                skipped.Add(new SkippedFile(file.Relative, reason));
            else
                scanned.Add(file);
        }

        var results = scanned
            .AsParallel()
            .AsOrdered()
            .Select(f => (File: f, Result: TryScan(f.Full, f.Relative)))
            .ToList();

        var occurrences = new List<StringOccurrence>();
        var scannedFiles = new List<string>();
        foreach (var (file, (found, error)) in results)
        {
            if (error is null)
            {
                scannedFiles.Add(file.Relative);
                occurrences.AddRange(found!);
            }
            else
            {
                skipped.Add(new SkippedFile(file.Relative, SkipReason.Unreadable, error));
            }
        }
        skipped.Sort((a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));

        return new ScanResult(occurrences, scannedFiles, skipped);
    }

    private static (IReadOnlyList<StringOccurrence>? Found, string? Error) TryScan(string fullPath, string relativePath)
    {
        try
        {
            return (ScanSource(File.ReadAllText(fullPath), fullPath, relativePath), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// Scans one file's source text. Results are ordered by position. Each interpolated string
    /// is one result; string literals nested inside its holes are separate results.
    /// </summary>
    public static IReadOnlyList<StringOccurrence> ScanSource(string code, string filePath = "", string relativePath = "")
    {
        var source = SourceText.From(code);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, path: filePath);
        var results = new List<StringOccurrence>();

        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            string? value = node switch
            {
                // "a", @"a", """a""", "a"u8
                LiteralExpressionSyntax literal when
                    literal.IsKind(SyntaxKind.StringLiteralExpression) ||
                    literal.IsKind(SyntaxKind.Utf8StringLiteralExpression) => literal.Token.ValueText,
                // $"Hello {name}"
                InterpolatedStringExpressionSyntax interpolated => InterpolatedValue(interpolated),
                _ => null,
            };

            if (value is null)
                continue;

            var start = source.Lines.GetLinePosition(node.Span.Start);
            results.Add(new StringOccurrence(
                filePath,
                relativePath,
                node.Span,
                start.Line + 1,
                start.Character + 1,
                node.ToString(),
                value,
                SnippetContext.FromSource(source, node.Span)));
        }

        // DescendantNodes is already pre-order (by start position); keep the sort as a guarantee.
        results.Sort((a, b) => a.Span.Start.CompareTo(b.Span.Start));
        return results;
    }

    private static string InterpolatedValue(InterpolatedStringExpressionSyntax node)
    {
        var sb = new StringBuilder();
        foreach (var content in node.Contents)
        {
            sb.Append(content switch
            {
                InterpolatedStringTextSyntax text => text.TextToken.ValueText,
                _ => content.ToString(), // InterpolationSyntax: keep "{expr,align:format}" as written
            });
        }
        return sb.ToString();
    }
}
