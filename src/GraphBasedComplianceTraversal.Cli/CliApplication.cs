namespace GraphBasedComplianceTraversal.Cli;

public static class CliApplication
{
    private const string Usage = """
        Graph-based compliance traversal

        Usage:
          compliance-traversal [--help]
          compliance-traversal demo

        Commands:
          demo          Run the end-to-end demonstration: load the repository's
                        declared facts and mocked external responses, build the
                        typed graph, traverse the three canonical paths, evaluate
                        the assertions, lint the structure, and compile the
                        explainable supply-chain narrative. Offline and
                        deterministic; see docs/demo.md.

        Options:
          -h, --help    Show this help text.
        """;

    public static int Run(IReadOnlyList<string> args, TextWriter output, TextWriter error) =>
        Run(args, output, error, repositoryRoot: null);

    public static int Run(
        IReadOnlyList<string> args,
        TextWriter output,
        TextWriter error,
        string? repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        if (args.Count == 0 || (args.Count == 1 && args[0] is "-h" or "--help" or "help"))
        {
            output.WriteLine(Usage);
            return 0;
        }

        if (args.Count == 1 && args[0] == "demo")
        {
            try
            {
                DemoPipeline.Run(ResolveRepositoryRoot(repositoryRoot), output);
                return 0;
            }
            catch (Exception exception)
            {
                error.WriteLine($"demo failed: {exception.Message}");
                return 1;
            }
        }

        error.WriteLine($"Unknown command or option: {args[0]}");
        error.WriteLine("Run with --help to see usage.");
        return 2;
    }

    /// <summary>
    /// The repository root the demo runs against: the explicitly provided
    /// root, or the nearest directory at or above the current working
    /// directory that holds the solution file.
    /// </summary>
    private static string ResolveRepositoryRoot(string? repositoryRoot)
    {
        if (repositoryRoot is not null)
        {
            return repositoryRoot;
        }

        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "GraphBasedComplianceTraversal.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                "Could not find the repository root (no GraphBasedComplianceTraversal.sln at or above the current directory).");
    }
}
