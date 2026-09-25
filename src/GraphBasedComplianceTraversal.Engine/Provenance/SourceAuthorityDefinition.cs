namespace GraphBasedComplianceTraversal.Engine.Provenance;

/// <summary>
/// The authoritative definition of the provenance slice this repository
/// compiles against: the claim types and the authority ranking each source
/// kind holds per claim type, with the rationale for every entry.
/// docs/provenance.md renders this table for humans; this class is the
/// contract code compiles against, and the test suite pins it.
/// </summary>
internal static class SourceAuthorityDefinition
{
    public static SourceAuthorityRegistry Create()
    {
        // Configuration facts: the live API observation is ground truth for
        // what is actually configured; Terraform state says what
        // infrastructure-as-code has applied (which can lag or drift); the
        // organization's record states intent.
        //
        // Derivation facts: the signed attestation binds the artifact digest
        // to the build that produced it; the CI record ties the build to its
        // run and head commit; code-host metadata records the commit and
        // repository; the organization's records state the intended build
        // path.
        //
        // Organizational facts: the organization's own governance records are
        // authoritative for its policies and decisions; code-host metadata
        // corroborates them.
        SourceAuthority[] authorities =
        [
            new(ClaimType.Configuration, SourceKind.AwsObserved, 1,
                "A live (here mocked) AWS API response is ground truth for the resource's actual configuration."),
            new(ClaimType.Configuration, SourceKind.Terraform, 2,
                "Terraform state records what infrastructure-as-code has applied; it can lag or drift from live state."),
            new(ClaimType.Configuration, SourceKind.Declaration, 3,
                "The organization's resource record states intended configuration, not applied state."),

            new(ClaimType.Derivation, SourceKind.ProvenanceAttestation, 1,
                "A signed SLSA provenance attestation cryptographically binds the artifact to the build that produced it."),
            new(ClaimType.Derivation, SourceKind.Ci, 2,
                "The CI workflow-run record ties the build to its run and head commit."),
            new(ClaimType.Derivation, SourceKind.Github, 3,
                "Code-host metadata records the source commit and repository."),
            new(ClaimType.Derivation, SourceKind.Declaration, 4,
                "The organization's pipeline records state the intended build path."),

            new(ClaimType.Organization, SourceKind.Declaration, 1,
                "The organization's governance records are authoritative for its own policies, adoptions, and approvals."),
            new(ClaimType.Organization, SourceKind.Github, 2,
                "Code-host metadata (for example CODEOWNERS) corroborates organizational claims."),
        ];

        return new SourceAuthorityRegistry(authorities);
    }
}
