using GraphBasedComplianceTraversal.Cli;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

public sealed class CliApplicationTests
{
    [Theory]
    [InlineData()]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public void HelpInvocationPrintsUsage(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CliApplication.Run(args, output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Usage:", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void HelpOutputMatchesGoldenFile()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CliApplication.Run(["--help"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Golden.AssertMatches("cli-help.txt", output.ToString());
    }

    [Fact]
    public void UnknownCommandReturnsNonZeroExitCode()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = CliApplication.Run(["unknown"], output, error);

        Assert.NotEqual(0, exitCode);
        Assert.Equal(string.Empty, output.ToString());
        Assert.Contains("Unknown command", error.ToString(), StringComparison.Ordinal);
    }
}
