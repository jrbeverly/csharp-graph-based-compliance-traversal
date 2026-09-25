using System.Globalization;
using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Pins provenance onto the graph's facts: every node, edge, and property can
/// answer "where did this come from?", conflicting sources consult the
/// declared authority table, and a fact with no known source is marked as
/// such rather than defaulted to authoritative. The scenarios use the
/// repository's own fixture world: the S3 bucket declaration, the Terraform
/// state fragment, the mocked AWS observation, the Sigstore attestation, and
/// the CI workflow-run record.
/// </summary>
public sealed class GraphProvenanceTests
{
    private const string BucketDeclarationLocator = "data/cloud/aws/resources/s3-prod-release-artifacts.yaml";
    private const string TerraformLocator = "fixtures/terraform/release-storage-resource.json";
    private const string AwsEncryptionLocator = "fixtures/aws/s3/get-bucket-encryption-response.json";
    private const string SigstoreAttestationLocator = "fixtures/provenance/sigstore-attestation.json";
    private const string CiWorkflowRunLocator = "fixtures/ci/workflow-run-response.json";

    [Fact]
    public void TheBucketEncryptionPropertyTracesToTheDeclarationTerraformAndAwsSources()
    {
        var graph = EmptyGraph();
        var bucket = graph.AddNode(
            "aws:s3:prod-release-artifacts",
            Node("S3Bucket"),
            new Dictionary<string, JsonNode?>
            {
                ["encryption"] = Json("""{"algorithm":"aws:kms"}"""),
            },
            new FactProvenance(new ProvenanceRecord(SourceKind.Declaration, BucketDeclarationLocator)));

        // The same fact — bucket encryption — is stated by three different
        // sources: the organization's resource record, the Terraform state
        // fragment, and the mocked AWS observation. Each statement is kept,
        // and each is traceable to its originating file.
        bucket.SetPropertyProvenance(
            "encryption",
            new ProvenanceRecord(SourceKind.Declaration, BucketDeclarationLocator),
            new ProvenanceRecord(SourceKind.Terraform, TerraformLocator),
            new ProvenanceRecord(SourceKind.AwsObserved, AwsEncryptionLocator));

        var provenance = bucket.GetPropertyProvenance("encryption");
        Assert.Equal(
            [SourceKind.Declaration, SourceKind.Terraform, SourceKind.AwsObserved],
            provenance.Sources.Select(record => record.Kind).ToArray());
        Assert.Equal(BucketDeclarationLocator, provenance.Sources[0].Locator);
        Assert.Equal(TerraformLocator, provenance.Sources[1].Locator);
        Assert.Equal(AwsEncryptionLocator, provenance.Sources[2].Locator);
        Assert.Same(provenance, bucket.PropertyProvenance["encryption"]);

        // The node itself traces to the record that declared it.
        Assert.Equal(SourceKind.Declaration, Assert.Single(bucket.Provenance.Sources).Kind);
        Assert.Equal(BucketDeclarationLocator, bucket.Provenance.Sources[0].Locator);

        // When the three statements disagree, the precedence to consult is
        // declared: the live AWS observation outranks Terraform state, which
        // outranks the organization's intended configuration.
        var registry = SourceAuthorityRegistry.Slice;
        Assert.Equal(SourceKind.AwsObserved, registry.MostAuthoritative(ClaimType.Configuration, provenance.Sources)!.Kind);
        Assert.Equal(SourcePrecedence.RightWins, registry.Compare(ClaimType.Configuration, SourceKind.Declaration, SourceKind.AwsObserved));
        Assert.Equal(SourcePrecedence.RightWins, registry.Compare(ClaimType.Configuration, SourceKind.Declaration, SourceKind.Terraform));
        Assert.Equal(SourcePrecedence.RightWins, registry.Compare(ClaimType.Configuration, SourceKind.Terraform, SourceKind.AwsObserved));
    }

    [Fact]
    public void TheDerivedEdgeFromArtifactToItsBuildTracesToTheProvenanceAndCiFixtures()
    {
        var graph = EmptyGraph();
        var artifact = graph.AddNode("artifact:customer-agent-2.8.4", Node("Artifact"));
        var pipeline = graph.AddNode("build-pipeline:customer-agent-release", Node("BuildPipeline"));

        // The derived edge the provenance and CI fixtures establish: the
        // Sigstore attestation binds the artifact digest to the build that
        // produced it, and the CI workflow-run record records that run and
        // its head commit. Neither fixture was written by hand as an edge —
        // the edge is derived from them, so it carries their provenance.
        var edge = graph.AddEdge(
            "produced-by",
            artifact,
            pipeline,
            new FactProvenance(
                new ProvenanceRecord(SourceKind.ProvenanceAttestation, SigstoreAttestationLocator, version: "slsa-provenance/v1"),
                new ProvenanceRecord(
                    SourceKind.Ci,
                    CiWorkflowRunLocator,
                    DateTimeOffset.Parse("2026-08-08T13:50:00Z", CultureInfo.InvariantCulture))));

        Assert.Equal(
            [SourceKind.ProvenanceAttestation, SourceKind.Ci],
            edge.Provenance.Sources.Select(record => record.Kind).ToArray());
        Assert.Equal(SigstoreAttestationLocator, edge.Provenance.Sources[0].Locator);
        Assert.Equal("slsa-provenance/v1", edge.Provenance.Sources[0].Version);
        Assert.Equal(CiWorkflowRunLocator, edge.Provenance.Sources[1].Locator);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-08T13:50:00Z", CultureInfo.InvariantCulture),
            edge.Provenance.Sources[1].Timestamp);

        // The same edge answers provenance from either traversal direction.
        Assert.Same(edge, Assert.Single(graph.GetEdges(artifact.Id, "produced-by")));
        Assert.Same(edge, Assert.Single(graph.GetEdges(pipeline.Id, "produces")));

        // For a derivation fact the attestation is the source to consult,
        // ahead of the CI record — by declared precedence, not by chance.
        var registry = SourceAuthorityRegistry.Slice;
        Assert.Equal(
            SourceKind.ProvenanceAttestation,
            registry.MostAuthoritative(ClaimType.Derivation, edge.Provenance.Sources)!.Kind);
        Assert.Equal(SourcePrecedence.LeftWins, registry.Compare(ClaimType.Derivation, SourceKind.ProvenanceAttestation, SourceKind.Ci));
        Assert.Equal(SourcePrecedence.RightWins, registry.Compare(ClaimType.Derivation, SourceKind.Declaration, SourceKind.Ci));
    }

    [Fact]
    public void FactsWithNoKnownSourceAreMarkedUnknownRatherThanDefaultedToAuthoritative()
    {
        var graph = EmptyGraph();
        var bucket = graph.AddNode("aws:s3:prod-release-artifacts", Node("S3Bucket"));
        var service = graph.AddNode("service:release-distribution", Node("Service"));
        var edge = graph.AddEdge("depends-on", service, bucket);

        // A node or edge added without provenance reads as unknown — it is
        // marked, and no query fabricates an authoritative source for it.
        Assert.True(bucket.Provenance.IsUnknown);
        Assert.False(bucket.Provenance.HasKnownSource);
        Assert.Empty(bucket.Provenance.Sources);
        Assert.True(edge.Provenance.IsUnknown);

        // A property that was never given provenance is marked unknown too.
        var encryptionProvenance = bucket.GetPropertyProvenance("encryption");
        Assert.True(encryptionProvenance.IsUnknown);
        Assert.Empty(encryptionProvenance.Sources);

        // Authority is never invented for an unknown source: there is no
        // source to consult and no precedence to award.
        var registry = SourceAuthorityRegistry.Slice;
        Assert.Null(registry.MostAuthoritative(ClaimType.Configuration, bucket.Provenance.Sources));
        Assert.Null(registry.MostAuthoritative(ClaimType.Configuration, encryptionProvenance.Sources));
        Assert.False(registry.IsAuthoritative(ClaimType.Configuration, SourceKind.Unknown));

        // Marking the unknown source explicitly is the same mark, not a source.
        bucket.SetPropertyProvenance("versioning", ProvenanceRecord.None);
        Assert.False(bucket.GetPropertyProvenance("versioning").HasKnownSource);
    }

    [Fact]
    public void ProvenanceIsAttachableThroughEveryAddNodeAndAddEdgeShape()
    {
        var graph = EmptyGraph();
        var artifact = graph.AddNode("artifact:customer-agent-2.8.4", Node("Artifact"));
        var bucket = graph.AddNode("aws:s3:prod-release-artifacts", Node("S3Bucket"));

        var storedIn = graph.AddEdge(
            "stored-in",
            artifact.Id,
            bucket.Id,
            new FactProvenance(new ProvenanceRecord(SourceKind.Declaration, BucketDeclarationLocator)));
        Assert.Equal(SourceKind.Declaration, Assert.Single(storedIn.Provenance.Sources).Kind);

        var observed = graph.AddEdge(
            "observes",
            "observation:obs-2026-08-08-001",
            Node("Observation"),
            artifact.Id,
            artifact.Type,
            new FactProvenance(new ProvenanceRecord(SourceKind.Declaration, "data/security/observations/provenance-observations.yaml")));
        Assert.Equal(SourceKind.Declaration, Assert.Single(observed.Provenance.Sources).Kind);
        Assert.False(observed.IsResolved); // the observation has no record; the dangling reference stays visible
    }

    [Fact]
    public void SettingPropertyProvenanceReplacesAndAnUnknownPropertyNameIsRejected()
    {
        var graph = EmptyGraph();
        var bucket = graph.AddNode("aws:s3:prod-release-artifacts", Node("S3Bucket"));

        bucket.SetPropertyProvenance("encryption", new ProvenanceRecord(SourceKind.Declaration, BucketDeclarationLocator));
        bucket.SetPropertyProvenance("encryption", new ProvenanceRecord(SourceKind.Terraform, TerraformLocator));

        // The last write wins, like a subsequent Declare or Observe call.
        Assert.Equal(
            SourceKind.Terraform,
            Assert.Single(bucket.GetPropertyProvenance("encryption").Sources).Kind);

        Assert.Throws<ArgumentException>(() => bucket.SetPropertyProvenance(" "));
        Assert.Throws<ArgumentException>(() => bucket.GetPropertyProvenance(" "));
    }

    private static TypedPropertyGraph EmptyGraph() => new(EdgeTypeRegistry.Slice);

    private static NodeType Node(string name) =>
        EdgeTypeRegistry.Slice.NodeTypes.Single(type => type.Name == name);

    private static JsonNode Json(string json) =>
        JsonNode.Parse(json) ?? throw new InvalidOperationException("Test value must parse to a non-null JSON node.");
}
