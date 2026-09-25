namespace GraphBasedComplianceTraversal.Engine.Provenance;

/// <summary>
/// The outcome of comparing two source kinds for a claim type against an
/// authority table. <see cref="Incomparable"/> is itself a defined result:
/// when neither source is declared authoritative for the claim type, no
/// precedence is invented.
/// </summary>
public enum SourcePrecedence
{
    /// <summary>Both sources carry the same authority for the claim type.</summary>
    Equal = 0,

    /// <summary>The left source outranks the right source.</summary>
    LeftWins,

    /// <summary>The right source outranks the left source.</summary>
    RightWins,

    /// <summary>Neither source is declared authoritative for the claim type; precedence is undefined, not arbitrary.</summary>
    Incomparable,
}
