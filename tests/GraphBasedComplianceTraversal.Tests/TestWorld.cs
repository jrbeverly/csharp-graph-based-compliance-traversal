using System.Runtime.CompilerServices;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// The two test worlds: the repository's own <c>data/</c> and <c>fixtures/</c>
/// trees (the real world) and the curated derived fixtures under
/// <c>Fixtures/</c> for negative-path tests. Neither world is written by tests.
/// </summary>
public static class TestWorld
{
    private static readonly string SourceDirectory = ResolveSourceDirectory();

    /// <summary>The repository checkout root that owns the real data/ and fixtures/ trees.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot(SourceDirectory);

    /// <summary>The curated derived fixtures directory for negative-path tests.</summary>
    public static string DerivedFixturesDirectory { get; } = Path.Combine(SourceDirectory, "Fixtures");

    /// <summary>A named derived fixture world under <see cref="DerivedFixturesDirectory"/>.</summary>
    public static string DerivedWorld(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return Path.Combine(DerivedFixturesDirectory, name);
    }

    private static string ResolveSourceDirectory([CallerFilePath] string? sourceFilePath = null) =>
        Path.GetDirectoryName(sourceFilePath)
        ?? throw new InvalidOperationException("The test project directory could not be resolved.");

    private static string FindRepositoryRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "GraphBasedComplianceTraversal.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
