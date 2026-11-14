using System.Text.RegularExpressions;

namespace StringSearch.Scanning;

public enum SkipReason
{
    /// <summary>Inside a build output or VCS directory (bin, obj, .git).</summary>
    BuildOutput,
    /// <summary>A directory or file name that looks like test code.</summary>
    Test,
    /// <summary>The file could not be read (permissions, deleted during the scan, ...).</summary>
    Unreadable,
}

/// <summary>Decides which source files are left out of a scan.</summary>
public static partial class PathFilter
{
    private static readonly string[] ExcludedDirectories = ["bin", "obj", ".git"];

    /// <summary>
    /// Matches a path segment (directory name, or file name without extension) that names test
    /// code. "test"/"tests" must be a whole word, so Latest, Contest, Attestation and Testing
    /// do not match. Case-sensitive on purpose: the PascalCase rules rely on it.
    /// <list type="number">
    /// <item>is test/tests in any case: tests, Tests, TEST</item>
    /// <item>ends with PascalCase Test/Tests: UnitTests, FooTests, ParserTest</item>
    /// <item>ends with a separator + test(s), any case: MyApp.Tests, foo_test, api-tests</item>
    /// <item>starts with test(s) + uppercase or separator: TestHelpers, Tests.Integration, test_utils</item>
    /// </list>
    /// </summary>
    [GeneratedRegex(@"^(?i:tests?)$|(?<=[a-z0-9])Tests?$|[._-](?i:tests?)$|^(?i:tests?)(?=[A-Z._-])")]
    private static partial Regex TestSegment();

    /// <summary>
    /// Returns why the file should be skipped, or null to keep it. Only
    /// <paramref name="relativePath"/> is inspected, so a target directory that itself lives
    /// under e.g. <c>bin/</c> or <c>Tests/</c> is not excluded wholesale.
    /// </summary>
    public static SkipReason? GetSkipReason(string relativePath)
    {
        var segments = relativePath.Split('/', '\\');
        var directories = segments.AsSpan(0, segments.Length - 1);

        foreach (var segment in directories)
        {
            foreach (var dir in ExcludedDirectories)
            {
                if (string.Equals(segment, dir, StringComparison.OrdinalIgnoreCase))
                    return SkipReason.BuildOutput;
            }
        }

        foreach (var segment in directories)
        {
            if (TestSegment().IsMatch(segment))
                return SkipReason.Test;
        }

        var fileStem = Path.GetFileNameWithoutExtension(segments[^1]);
        return TestSegment().IsMatch(fileStem) ? SkipReason.Test : null;
    }

    public static bool IsExcluded(string relativePath) => GetSkipReason(relativePath) is not null;
}
