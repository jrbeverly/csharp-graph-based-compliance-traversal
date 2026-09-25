using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Asserts that actual output matches a checked-in golden file under
/// <c>Golden/</c> and provides the single, intentional way to update goldens:
/// run the suite with <c>UPDATE_GOLDENS=1</c> (or <c>make test-update-goldens</c>).
/// Golden files are plain text, so every update is reviewable as an ordinary diff.
/// </summary>
public static class Golden
{
    private static readonly string GoldenDirectory = ResolveGoldenDirectory();
    private static readonly bool UpdateGoldens =
        Environment.GetEnvironmentVariable("UPDATE_GOLDENS") == "1";

    /// <summary>Asserts that <paramref name="actual"/> matches the golden file <paramref name="name"/>.</summary>
    public static void AssertMatches(string name, string actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        AssertMatches(name, Encoding.UTF8.GetBytes(actual));
    }

    /// <summary>Asserts that <paramref name="actual"/> matches the golden file <paramref name="name"/>.</summary>
    public static void AssertMatches(string name, byte[] actual)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(actual);

        var goldenPath = Path.Combine(GoldenDirectory, name);
        if (UpdateGoldens)
        {
            File.WriteAllBytes(goldenPath, actual);
        }

        var expected = File.ReadAllBytes(goldenPath);
        Assert.True(
            expected.AsSpan().SequenceEqual(actual),
            $"Golden file '{name}' does not match the actual output. " +
            "If the change is intended, regenerate it with 'make test-update-goldens' " +
            "(or UPDATE_GOLDENS=1) and review the diff.");
    }

    private static string ResolveGoldenDirectory([CallerFilePath] string? sourceFilePath = null)
    {
        var directory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("The golden directory could not be resolved.");
        return Path.Combine(directory, "Golden");
    }
}
