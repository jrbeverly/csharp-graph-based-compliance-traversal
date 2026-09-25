using System.Globalization;
using GraphBasedComplianceTraversal.Engine.Provenance;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

public sealed class ProvenanceTests
{
    // Pins the shared provenance model consumed by later milestones; grow
    // these lists only by extending the enums and updating docs/provenance.md.
    private static readonly string[] ExpectedSourceKindNames =
        ["Unknown", "Declaration", "Terraform", "AwsObserved", "Github", "Ci", "ProvenanceAttestation"];
    private static readonly string[] ExpectedClaimTypeNames =
        ["Configuration", "Derivation", "Organization"];

    [Fact]
    public void TheSourceAndClaimEnumerationsHaveExactlyTheDeclaredMembersWithUnknownAsTheDefault()
    {
        Assert.Equal(ExpectedSourceKindNames, Enum.GetNames<SourceKind>());
        Assert.Equal(ExpectedClaimTypeNames, Enum.GetNames<ClaimType>());

        // A provenance slot that was never filled reads Unknown, never a
        // fabricated authoritative source, by construction.
        Assert.Equal(SourceKind.Unknown, default(SourceKind));
    }

    [Fact]
    public void AKnownSourceCarriesItsLocatorAndOptionalTimestampAndVersion()
    {
        var record = new ProvenanceRecord(
            SourceKind.Ci,
            "fixtures/ci/workflow-run-response.json",
            DateTimeOffset.Parse("2026-08-08T13:50:00Z", CultureInfo.InvariantCulture),
            "workflow-run/v1");

        Assert.Equal(SourceKind.Ci, record.Kind);
        Assert.Equal("fixtures/ci/workflow-run-response.json", record.Locator);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-08T13:50:00Z", CultureInfo.InvariantCulture),
            record.Timestamp);
        Assert.Equal("workflow-run/v1", record.Version);
        Assert.True(record.HasKnownSource);
    }

    [Fact]
    public void AKnownSourceWithoutALocatorIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ProvenanceRecord(SourceKind.Declaration, " "));
        Assert.Throws<ArgumentException>(() => new ProvenanceRecord(SourceKind.Terraform, ""));
        Assert.Throws<ArgumentException>(
            () => new ProvenanceRecord(SourceKind.Ci, "fixtures/ci/workflow-run-response.json", version: " "));
    }

    [Fact]
    public void NoneIsTheExplicitRecordOfAFactWithNoKnownSource()
    {
        var none = ProvenanceRecord.None;

        Assert.Equal(SourceKind.Unknown, none.Kind);
        Assert.Equal(string.Empty, none.Locator);
        Assert.Null(none.Timestamp);
        Assert.Null(none.Version);
        Assert.False(none.HasKnownSource);
    }

    [Fact]
    public void AnEmptyProvenanceIsExplicitlyUnknownRatherThanAuthoritative()
    {
        var provenance = FactProvenance.None;

        Assert.Empty(provenance.Sources);
        Assert.False(provenance.HasKnownSource);
        Assert.True(provenance.IsUnknown);

        // A collection holding only the no-known-source marker is equally
        // unknown: the marker contributes nothing.
        var marked = new FactProvenance(ProvenanceRecord.None);
        Assert.False(marked.HasKnownSource);
        Assert.True(marked.IsUnknown);
    }

    [Fact]
    public void FactProvenanceKeepsItsRecordsInAttachmentOrder()
    {
        var declaration = new ProvenanceRecord(SourceKind.Declaration, "data/cloud/aws/resources/s3-prod-release-artifacts.yaml");
        var terraform = new ProvenanceRecord(SourceKind.Terraform, "fixtures/terraform/release-storage-resource.json");

        var provenance = new FactProvenance(declaration, terraform);

        Assert.Equal([declaration, terraform], provenance.Sources);
        Assert.True(provenance.HasKnownSource);
        Assert.False(provenance.IsUnknown);
    }

    [Fact]
    public void FactProvenanceRejectsNullRecords()
    {
        Assert.Throws<ArgumentNullException>(() => new FactProvenance(ProvenanceRecord.None, null!));
    }

    [Fact]
    public void TheSliceAuthorityTableHasExactlyTheDeclaredShape()
    {
        var registry = SourceAuthorityRegistry.Slice;

        // Pins the authoritative table; grow these numbers only by extending
        // SourceAuthorityDefinition and updating docs/provenance.md.
        Assert.Equal(9, registry.Authorities.Count);

        // Configuration: live observed state outranks applied-as-code state,
        // which outranks the organization's intended configuration.
        Assert.Equal(
            [SourceKind.AwsObserved, SourceKind.Terraform, SourceKind.Declaration],
            registry.RankedKinds(ClaimType.Configuration));

        // Derivation: the signed attestation outranks the CI record, which
        // outranks code-host metadata, which outranks the organization's
        // stated build path.
        Assert.Equal(
            [SourceKind.ProvenanceAttestation, SourceKind.Ci, SourceKind.Github, SourceKind.Declaration],
            registry.RankedKinds(ClaimType.Derivation));

        // Organization: the organization's own governance records are
        // authoritative; code-host metadata corroborates.
        Assert.Equal(
            [SourceKind.Declaration, SourceKind.Github],
            registry.RankedKinds(ClaimType.Organization));
    }

    [Fact]
    public void RanksAreOneBasedWithOneBeingMostAuthoritative()
    {
        var registry = SourceAuthorityRegistry.Slice;

        Assert.Equal(1, registry.RankFor(ClaimType.Configuration, SourceKind.AwsObserved));
        Assert.Equal(2, registry.RankFor(ClaimType.Configuration, SourceKind.Terraform));
        Assert.Equal(3, registry.RankFor(ClaimType.Configuration, SourceKind.Declaration));
        Assert.True(registry.IsAuthoritative(ClaimType.Configuration, SourceKind.Declaration));
    }

    [Fact]
    public void ASourceKindWithoutAnEntryHasNoAuthorityForTheClaimType()
    {
        var registry = SourceAuthorityRegistry.Slice;

        // A CI record says nothing about a resource's configuration.
        Assert.Null(registry.RankFor(ClaimType.Configuration, SourceKind.Ci));
        Assert.False(registry.IsAuthoritative(ClaimType.Configuration, SourceKind.Ci));

        // No known source is authoritative anywhere, by construction.
        Assert.Null(registry.RankFor(ClaimType.Configuration, SourceKind.Unknown));
        Assert.False(registry.IsAuthoritative(ClaimType.Derivation, SourceKind.Unknown));
    }

    [Fact]
    public void PrecedenceIsExplicitForEveryComparison()
    {
        var registry = SourceAuthorityRegistry.Slice;

        // Observed cloud state outranks a declaration for configuration facts.
        Assert.Equal(
            SourcePrecedence.LeftWins,
            registry.Compare(ClaimType.Configuration, SourceKind.AwsObserved, SourceKind.Declaration));
        Assert.Equal(
            SourcePrecedence.RightWins,
            registry.Compare(ClaimType.Configuration, SourceKind.Declaration, SourceKind.Terraform));
        Assert.Equal(
            SourcePrecedence.RightWins,
            registry.Compare(ClaimType.Configuration, SourceKind.Terraform, SourceKind.AwsObserved));

        // The attestation outranks every other derivation source.
        Assert.Equal(
            SourcePrecedence.LeftWins,
            registry.Compare(ClaimType.Derivation, SourceKind.ProvenanceAttestation, SourceKind.Ci));
        Assert.Equal(
            SourcePrecedence.LeftWins,
            registry.Compare(ClaimType.Derivation, SourceKind.Ci, SourceKind.Declaration));

        // A source compared with itself is equal, never a win.
        Assert.Equal(
            SourcePrecedence.Equal,
            registry.Compare(ClaimType.Configuration, SourceKind.AwsObserved, SourceKind.AwsObserved));

        // Neither source ranked for the claim type: precedence is undefined
        // and says so, rather than picking one arbitrarily.
        Assert.Equal(
            SourcePrecedence.Incomparable,
            registry.Compare(ClaimType.Configuration, SourceKind.Ci, SourceKind.Github));
        Assert.Equal(
            SourcePrecedence.Incomparable,
            registry.Compare(ClaimType.Organization, SourceKind.Unknown, SourceKind.Ci));
    }

    [Fact]
    public void AnUnknownSourceNeverWinsAComparison()
    {
        var registry = SourceAuthorityRegistry.Slice;

        Assert.Equal(
            SourcePrecedence.RightWins,
            registry.Compare(ClaimType.Configuration, SourceKind.Unknown, SourceKind.Declaration));
        Assert.Equal(
            SourcePrecedence.LeftWins,
            registry.Compare(ClaimType.Derivation, SourceKind.ProvenanceAttestation, SourceKind.Unknown));
    }

    [Fact]
    public void MostAuthoritativePicksTheHighestRankedSourceToConsult()
    {
        var registry = SourceAuthorityRegistry.Slice;
        var declaration = new ProvenanceRecord(SourceKind.Declaration, "data/cloud/aws/resources/s3-prod-release-artifacts.yaml");
        var terraform = new ProvenanceRecord(SourceKind.Terraform, "fixtures/terraform/release-storage-resource.json");
        var observed = new ProvenanceRecord(SourceKind.AwsObserved, "fixtures/aws/s3/get-bucket-encryption-response.json");

        Assert.Same(observed, registry.MostAuthoritative(ClaimType.Configuration, [declaration, terraform, observed]));
        Assert.Same(terraform, registry.MostAuthoritative(ClaimType.Configuration, [declaration, terraform]));
        Assert.Same(declaration, registry.MostAuthoritative(ClaimType.Configuration, [declaration]));

        // A no-known-source marker is skipped, never picked.
        Assert.Same(observed, registry.MostAuthoritative(ClaimType.Configuration, [ProvenanceRecord.None, observed]));
    }

    [Fact]
    public void MostAuthoritativeReturnsNothingWhenNoSourceHasAuthority()
    {
        var registry = SourceAuthorityRegistry.Slice;

        Assert.Null(registry.MostAuthoritative(ClaimType.Configuration, []));

        // A CI record carries no authority over configuration facts.
        Assert.Null(registry.MostAuthoritative(
            ClaimType.Configuration,
            [new ProvenanceRecord(SourceKind.Ci, "fixtures/ci/workflow-run-response.json")]));

        // A fact with only a no-known-source marker has no authority to consult.
        Assert.Null(registry.MostAuthoritative(ClaimType.Organization, [ProvenanceRecord.None]));
    }

    [Fact]
    public void DeclaringTheUnknownKindAsAuthoritativeIsRejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => new SourceAuthorityRegistry(
        [
            new SourceAuthority(ClaimType.Configuration, SourceKind.Unknown, 1, "Must never be ranked."),
        ]));

        Assert.Contains("never", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaringTheSameKindOrRankTwiceForAClaimTypeIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new SourceAuthorityRegistry(
        [
            new SourceAuthority(ClaimType.Configuration, SourceKind.Declaration, 1, "Once."),
            new SourceAuthority(ClaimType.Configuration, SourceKind.Declaration, 2, "Twice."),
        ]));

        Assert.Throws<ArgumentException>(() => new SourceAuthorityRegistry(
        [
            new SourceAuthority(ClaimType.Configuration, SourceKind.Declaration, 1, "First."),
            new SourceAuthority(ClaimType.Configuration, SourceKind.Terraform, 1, "Tied: precedence would be arbitrary."),
        ]));
    }

    [Fact]
    public void ANonPositiveRankOrABlankRationaleIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new SourceAuthorityRegistry(
        [
            new SourceAuthority(ClaimType.Configuration, SourceKind.Declaration, 0, "Ranks are 1-based."),
        ]));

        Assert.Throws<ArgumentException>(() => new SourceAuthorityRegistry(
        [
            new SourceAuthority(ClaimType.Configuration, SourceKind.Declaration, 1, " "),
        ]));
    }
}
