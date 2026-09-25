namespace GraphBasedComplianceTraversal.Engine.Provenance;

/// <summary>
/// One entry of the authority table: the rank a source kind holds for one
/// claim type, with the rationale for the ranking. Rank 1 is the most
/// authoritative source; a source kind without an entry for a claim type has
/// no authority over it at all.
/// </summary>
public sealed record SourceAuthority(
    ClaimType ClaimType,
    SourceKind Kind,
    int Rank,
    string Rationale);
