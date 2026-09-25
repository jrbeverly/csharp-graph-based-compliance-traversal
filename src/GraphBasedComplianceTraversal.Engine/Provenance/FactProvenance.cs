namespace GraphBasedComplianceTraversal.Engine.Provenance;

/// <summary>
/// The provenance attached to one fact — a node, an edge, or a property: the
/// ordered set of records saying where the fact came from. A fact with no
/// records (or only <see cref="ProvenanceRecord.None"/> markers) is explicitly
/// unknown: it has no known source, and no query may promote it to
/// authoritative. Attaching records never overwrites or resolves the fact's
/// value; which source to consult for a claim type is the authority table's
/// answer, not this collection's.
/// </summary>
public sealed class FactProvenance
{
    private readonly IReadOnlyList<ProvenanceRecord> _sources;

    /// <summary>The provenance of a fact with no known source.</summary>
    public static FactProvenance None { get; } = new();

    /// <summary>
    /// Creates a provenance collection from the given records, kept in the
    /// order given. A record may be the explicit
    /// <see cref="ProvenanceRecord.None"/> marker; it contributes no known
    /// source.
    /// </summary>
    public FactProvenance(params ProvenanceRecord[] records)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (var record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
        }

        _sources = records.ToArray();
    }

    /// <summary>The records in attachment order. Empty for a fact with no recorded source.</summary>
    public IReadOnlyList<ProvenanceRecord> Sources => _sources;

    /// <summary>True when any record names a known source.</summary>
    public bool HasKnownSource => _sources.Any(record => record.HasKnownSource);

    /// <summary>
    /// True when no record names a known source — the fact is marked as having
    /// an unknown source, and is never treated as authoritative.
    /// </summary>
    public bool IsUnknown => !HasKnownSource;
}
