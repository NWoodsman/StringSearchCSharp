using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace StringSearch.Model;

/// <summary>A source file's text plus the encoding facts shown in the status bar.</summary>
public sealed class SourceDocument
{
    private SourceDocument(string path, SourceText text, string encodingName, string lineEnding)
    {
        Path = path;
        Text = text;
        EncodingName = encodingName;
        LineEnding = lineEnding;
    }

    public string Path { get; }
    public SourceText Text { get; }

    /// <summary>"UTF-8", "UTF-8 BOM", "UTF-16 LE" or "UTF-16 BE".</summary>
    public string EncodingName { get; }

    /// <summary>"LF", "CRLF", "CR", "Mixed" or "None" (single-line file).</summary>
    public string LineEnding { get; }

    public int LineCount => Text.Lines.Count;

    /// <summary>
    /// Decodes like <see cref="File.ReadAllText(string)"/> (BOM detection, BOM stripped), so spans
    /// found by <c>StringScanner</c> line up with this text.
    /// </summary>
    public static SourceDocument Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();
        return FromContent(path, content, DescribeEncoding(bytes));
    }

    /// <summary>The same file with different text (an edit buffer); encoding facts are kept.</summary>
    public SourceDocument WithText(SourceText text) => new(Path, text, EncodingName, LineEnding);

    public static SourceDocument FromContent(string path, string content, string encodingName = "UTF-8") =>
        new(path, SourceText.From(content), encodingName, DescribeLineEndings(content));

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
    private readonly Func<string, SourceDocument> _loader = loader ?? SourceDocument.Load;

    public SourceDocument Get(string path)
    {
        if (!_documents.TryGetValue(path, out var document))
            _documents[path] = document = _loader(path);
        return document;
    }
}
