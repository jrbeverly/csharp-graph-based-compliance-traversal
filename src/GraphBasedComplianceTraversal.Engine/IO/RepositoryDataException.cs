namespace GraphBasedComplianceTraversal.Engine.IO;

public sealed class RepositoryDataException : Exception
{
    public RepositoryDataException(string path, string message, Exception? innerException = null)
        : base($"Unable to load '{path}': {message}", innerException)
    {
        Path = path;
    }

    public string Path { get; }
}
