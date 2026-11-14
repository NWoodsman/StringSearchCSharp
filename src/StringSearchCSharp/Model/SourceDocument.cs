using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace StringSearch.Model;

/// <summary>A source file's text plus the encoding facts shown in the status bar.</summary>
public sealed class SourceDocument
{
    private SourceDocument(string path, SourceText text, string encodingName, string lineEnding, byte[]? contentHash,
        string? loadError = null)
    {
        LoadError = loadError;
        Path = path;
        Text = text;
        EncodingName = encodingName;
        LineEnding = lineEnding;
        ContentHash = contentHash;
    }

    public string Path { get; }
    public SourceText Text { get; }

    /// <summary>"UTF-8", "UTF-8 BOM", "UTF-16 LE" or "UTF-16 BE".</summary>
    public string EncodingName { get; }

    /// <summary>"LF", "CRLF", "CR", "Mixed" or "None" (single-line file).</summary>
    public string LineEnding { get; }

    public int LineCount => Text.Lines.Count;

    /// <summary>Why the file could not be read; the document is then empty and cannot be edited.</summary>
    public string? LoadError { get; }

    /// <summary>
    /// True when <paramref name="occurrence"/>'s span still holds its literal, i.e. the file has
    /// not changed since the scan. If it has, positions are stale and editing is refused.
    /// </summary>
    public bool Matches(StringOccurrence occurrence) =>
        LoadError is null && occurrence.Span.End <= Text.Length && Text.ToString(occurrence.Span) == occurrence.RawText;

    /// <summary>Placeholder for a file that could not be read.</summary>
    public static SourceDocument Unreadable(string path, string error) =>
        new(path, SourceText.From(""), "?", "None", null, error);

    /// <summary>SHA-256 of the file's bytes when it was loaded; null for documents not read from disk.</summary>
    public byte[]? ContentHash { get; }

    /// <summary>True when the file on disk still has the bytes this document was loaded from.</summary>
    public bool IsUnchangedOnDisk() =>
        ContentHash is null || (File.Exists(Path) && SHA256.HashData(File.ReadAllBytes(Path)).AsSpan().SequenceEqual(ContentHash));

    /// <summary>
    /// Decodes like <see cref="File.ReadAllText(string)"/> (BOM detection, BOM stripped), so spans
    /// found by <c>StringScanner</c> line up with this text.
    /// </summary>
    public static SourceDocument Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();
        return new SourceDocument(path, SourceText.From(content), DescribeEncoding(bytes), DescribeLineEndings(content),
            SHA256.HashData(bytes));
    }

    /// <summary>The same file with different text (an edit buffer); encoding facts are kept.</summary>
    public SourceDocument WithText(SourceText text) => new(Path, text, EncodingName, LineEnding, ContentHash);

    public static SourceDocument FromContent(string path, string content, string encodingName = "UTF-8") =>
        new(path, SourceText.From(content), encodingName, DescribeLineEndings(content), null);

    public static string DescribeEncoding(ReadOnlySpan<byte> bytes) => bytes switch
    {
        [0xEF, 0xBB, 0xBF, ..] => "UTF-8 BOM",
        [0xFF, 0xFE, ..] => "UTF-16 LE",
        [0xFE, 0xFF, ..] => "UTF-16 BE",
        _ => "UTF-8",
    };

    public static string DescribeLineEndings(string content)
    {
        int crlf = 0, lf = 0, cr = 0;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\r')
            {
                if (i + 1 < content.Length && content[i + 1] == '\n') { crlf++; i++; }
                else cr++;
            }
            else if (content[i] == '\n')
            {
                lf++;
            }
        }

        int kinds = (crlf > 0 ? 1 : 0) + (lf > 0 ? 1 : 0) + (cr > 0 ? 1 : 0);
        return kinds switch
        {
            0 => "None",
            > 1 => "Mixed",
            _ => crlf > 0 ? "CRLF" : lf > 0 ? "LF" : "CR",
        };
    }
}

/// <summary>Loads each source file once per session.</summary>
public sealed class SourceCache(Func<string, SourceDocument>? loader = null)
{
    private readonly Dictionary<string, SourceDocument> _documents = new(StringComparer.Ordinal);
    private readonly Func<string, SourceDocument> _loader = loader ?? LoadOrPlaceholder;

    /// <summary>Loads a file; a file that vanished or can't be read becomes a placeholder instead of an exception.</summary>
    public static SourceDocument LoadOrPlaceholder(string path)
    {
        try
        {
            return SourceDocument.Load(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return SourceDocument.Unreadable(path, ex.Message);
        }
    }

    /// <summary>Replaces a file's cached document (after it was written).</summary>
    public void Set(SourceDocument document) => _documents[document.Path] = document;

    public SourceDocument Get(string path)
    {
        if (!_documents.TryGetValue(path, out var document))
            _documents[path] = document = _loader(path);
        return document;
    }
}
