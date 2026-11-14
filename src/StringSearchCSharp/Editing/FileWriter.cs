using System.Text;

namespace StringSearch.Editing;

/// <summary>Writes source text back to disk with the file's original encoding and BOM.</summary>
public static class FileWriter
{
    /// <summary>
    /// Encoding for a <see cref="Model.SourceDocument.EncodingName"/>. Line endings need no
    /// handling here: untouched text keeps its own, and Enter inserts the file's line ending.
    /// </summary>
    public static Encoding EncodingFor(string encodingName) => encodingName switch
    {
        "UTF-8 BOM" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        "UTF-16 LE" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
        "UTF-16 BE" => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
        _ => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
    };

    public static byte[] Encode(string text, string encodingName)
    {
        var encoding = EncodingFor(encodingName);
        var preamble = encoding.GetPreamble();
        var body = encoding.GetBytes(text);
        var bytes = new byte[preamble.Length + body.Length];
        preamble.CopyTo(bytes, 0);
        body.CopyTo(bytes, preamble.Length);
        return bytes;
    }

    /// <summary>
    /// Replaces <paramref name="path"/> atomically: write a temp file next to it, copy the
    /// original's Unix permissions, then rename over it.
    /// </summary>
    public static void Write(string path, string text, string encodingName)
    {
        var temp = path + ".stringsearch.tmp";
        try
        {
            File.WriteAllBytes(temp, Encode(text, encodingName));
            if (!OperatingSystem.IsWindows() && File.Exists(path))
                File.SetUnixFileMode(temp, File.GetUnixFileMode(path));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
