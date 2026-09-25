namespace GraphBasedComplianceTraversal.Engine.Provenance;

/// <summary>
/// The kind of claim a fact makes, used to look up which sources are
/// authoritative for it. Different kinds of facts are naturally backed by
/// different evidence: the live state of a resource configuration answers to
/// an API observation, an artifact's derivation answers to its attestation,
/// and an organizational statement answers to the organization's own records.
/// </summary>
public enum ClaimType
{
    /// <summary>A configuration fact about a cloud or infrastructure resource (encryption, region, access, ...).</summary>
    Configuration,

    /// <summary>A derivation fact linking an artifact to its build, source commit, or repository.</summary>
    Derivation,

    /// <summary>An organizational fact: a policy, adoption, approval, ownership, or acceptance.</summary>
    Organization,
}
