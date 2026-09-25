namespace GraphBasedComplianceTraversal.Engine.Adapters;

/// <summary>
/// Thrown when an external-source adapter cannot normalize a parsed response:
/// either the response matched the adapter's shape but is not a well-formed
/// response of that shape, or a document the response references (for example
/// the workflow run an attestation names) is not available. It never wraps
/// I/O or network failures — adapters read only the parsed content handed to
/// them.
/// </summary>
public sealed class AdapterException : Exception
{
    public AdapterException(string documentPath, string message)
        : base($"Unable to normalize '{documentPath}': {message}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Path = documentPath;
    }

    /// <summary>The locator (repository-relative path) of the response that could not be normalized.</summary>
    public string Path { get; }
}
