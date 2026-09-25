namespace GraphBasedComplianceTraversal.Engine.Observations;

/// <summary>
/// Thrown when a recorded observation history entry cannot be ingested into
/// the <see cref="ObservationStore"/>: a field the entry must carry is
/// missing or malformed — an <c>observed_at</c> value that is not an ISO-8601
/// timestamp, a <c>result</c> other than <c>pass</c>/<c>fail</c>, a
/// <c>validator_version</c> that does not name a validator and a dotted
/// version, or a <c>subject</c> that does not name the observed artifact. A
/// malformed entry fails loudly, naming the history document's
/// repository-relative path — recorded history is never silently skipped.
/// </summary>
public sealed class ObservationRecordException : Exception
{
    public ObservationRecordException(string recordPath, string message)
        : base($"Unable to ingest '{recordPath}': {message}")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Path = recordPath;
    }

    /// <summary>The locator (repository-relative path) of the history document that could not be ingested.</summary>
    public string Path { get; }
}
