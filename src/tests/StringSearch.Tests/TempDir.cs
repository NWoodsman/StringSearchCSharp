namespace StringSearch.Tests;

/// <summary>A temporary directory deleted on dispose.</summary>
internal sealed class TempDir : IDisposable
{
    public DirectoryInfo Info { get; } = Directory.CreateTempSubdirectory("stringsearch-");

    public string Path => Info.FullName;

    public string Write(string relativePath, string content)
    {
        var path = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose() => Info.Delete(recursive: true);
}
