namespace GraphBasedComplianceTraversal.Engine.Provenance;

/// <summary>
/// The kind of source a fact was recorded from. <see cref="Unknown"/> is the
/// default value of the enumeration: a provenance slot that was never filled
/// reads as Unknown, so a fact with no known source is marked as such and can
/// never be mistaken for an authoritative statement. Every other member names
/// a source the repository's world actually carries: organizational records,
/// the mock Terraform state fragment, the mocked AWS API observations, the
/// mock GitHub API responses, the mock CI workflow-run record, and the mock
/// Sigstore provenance attestation.
/// </summary>
public enum SourceKind
{
    /// <summary>No known source: the fact's origin is not recorded. Never authoritative, by construction.</summary>
    Unknown = 0,

    /// <summary>An organizational record under <c>data/**</c> (a policy, adoption, resource declaration, ...).</summary>
    Declaration,

    /// <summary>An infrastructure-as-code statement from Terraform state.</summary>
    Terraform,

    /// <summary>An observation read from a cloud API response (mocked AWS fixtures in this world).</summary>
    AwsObserved,

    /// <summary>Metadata from a code-host API response (mocked GitHub fixtures in this world).</summary>
    Github,

    /// <summary>A CI workflow-run record (mocked CI fixtures in this world).</summary>
    Ci,

    /// <summary>A provenance attestation (a mocked Sigstore/SLSA statement in this world).</summary>
    ProvenanceAttestation,
}
