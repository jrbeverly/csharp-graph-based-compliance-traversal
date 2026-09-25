namespace GraphBasedComplianceTraversal.Engine.Provenance;

/// <summary>
/// The provenance of one fact (a node, an edge, or a property): the
/// <see cref="SourceKind"/> the fact was recorded from, the locator of the
/// originating file or fixture, and — where the source provides one — a
/// timestamp and a version. <see cref="None"/> is the explicit record of a
/// fact with no known source; it is a marker, never evidence, and no
/// authority table may rank it.
/// </summary>
public sealed record ProvenanceRecord
{
    /// <summary>
    /// Creates a provenance record. Any source other than
    /// <see cref="SourceKind.Unknown"/> must carry a locator; an Unknown
    /// record may carry a locator or be bare. The timestamp and version are
    /// recorded only where the source provides them.
    /// </summary>
    public ProvenanceRecord(SourceKind kind, string locator, DateTimeOffset? timestamp = null, string? version = null)
    {
        if (kind != SourceKind.Unknown)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(locator);
        }

        if (version is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(version);
        }

        Kind = kind;
        Locator = locator ?? string.Empty;
        Timestamp = timestamp;
        Version = version;
    }

    /// <summary>The explicit record for a fact whose source is not known.</summary>
    public static ProvenanceRecord None { get; } = new(SourceKind.Unknown, string.Empty);

    /// <summary>The kind of source the fact was recorded from.</summary>
    public SourceKind Kind { get; }

    /// <summary>
    /// The originating file or fixture, repository-relative (for example
    /// <c>data/cloud/aws/resources/s3-prod-release-artifacts.yaml</c> or
    /// <c>fixtures/aws/s3/get-bucket-encryption-response.json</c>). Empty for
    /// <see cref="None"/>.
    /// </summary>
    public string Locator { get; }

    /// <summary>The point in time the source recorded the fact, when the source provides one.</summary>
    public DateTimeOffset? Timestamp { get; }

    /// <summary>The version of the source that recorded the fact, when the source provides one.</summary>
    public string? Version { get; }

    /// <summary>True when the record names a known source rather than marking an unknown one.</summary>
    public bool HasKnownSource => Kind != SourceKind.Unknown;
}
