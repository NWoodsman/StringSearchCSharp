using StringSearch.Scanning;

namespace StringSearch.Tests.Scanning;

public class PathFilterTests
{
    [Theory]
    [InlineData("bin/A.cs")]
    [InlineData("src/obj/Debug/A.cs")]
    [InlineData("src/Bin/A.cs")]
    [InlineData(@"src\obj\A.cs")]
    [InlineData(".git/hooks/A.cs")]
    public void Skips_build_output_directories(string path) =>
        Assert.Equal(SkipReason.BuildOutput, PathFilter.GetSkipReason(path));

    [Theory]
    [InlineData("tests/A.cs")]
    [InlineData("Tests/A.cs")]
    [InlineData("TEST/A.cs")]
    [InlineData("src/UnitTests/A.cs")]
    [InlineData("src/FooTests.cs")]
    [InlineData("src/ParserTest.cs")]
    [InlineData("MyApp.Tests/A.cs")]
    [InlineData("src/Foo.Tests.cs")]
    [InlineData("src/foo_test.cs")]
    [InlineData("api-tests/A.cs")]
    [InlineData("src/TestHelpers/A.cs")]
    [InlineData("Tests.Integration/A.cs")]
    [InlineData("src/test_utils.cs")]
    [InlineData("src/Test.cs")]
    public void Skips_test_directories_and_files(string path) =>
        Assert.Equal(SkipReason.Test, PathFilter.GetSkipReason(path));

    [Theory]
    [InlineData("A.cs")]
    [InlineData("src/Latest.cs")]
    [InlineData("src/LatestVersion/A.cs")]
    [InlineData("Contest/A.cs")]
    [InlineData("src/Attestation.cs")]
    [InlineData("src/Testing.cs")]
    [InlineData("testimonials/A.cs")]
    [InlineData("src/bin.cs")]
    [InlineData("src/Binary/A.cs")]
    [InlineData("src/objects/A.cs")]
    public void Keeps_other_paths(string path) =>
        Assert.Null(PathFilter.GetSkipReason(path));
}
