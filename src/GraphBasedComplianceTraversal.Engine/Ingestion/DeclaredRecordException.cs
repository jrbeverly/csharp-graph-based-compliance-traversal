namespace GraphBasedComplianceTraversal.Engine.Ingestion;

/// <summary>
/// Thrown when an organizational record under <c>data/**</c> cannot be loaded
/// as a declared fact: the record's shape is not a typed record or an
/// observations history, its <c>type</c> is not declared by the ontology
/// slice, its <c>id</c> does not carry the type's prefix, or a reference
/// field's values do not match the field's declared mapping. A malformed
/// record fails loudly, naming its repository-relative path — it is never
/// silently skipped.
/// </summary>
public sealed class DeclaredRecordException : Exception
{
    public DeclaredRecordException(string recordPath, string message)
        : base($"Unable to ingest '{recordPath}': {message}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Path = recordPath;
    }

    /// <summary>The locator (repository-relative path) of the record that could not be loaded.</summary>
    public string Path { get; }
}
