using System.Globalization;
using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Adapters;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Pins what each concrete adapter normalizes its fixture into: the observed
/// or applied facts per source system, the subject identities, and the
/// artifact→build-run→commit→repository linkage the provenance attestation
/// and the CI record establish together. The scenarios run against the
/// repository's own fixture world.
/// </summary>
public sealed class FixtureAdapterTests
{
    private const string AwsEncryptionLocator = "fixtures/aws/s3/get-bucket-encryption-response.json";
    private const string AwsPolicyLocator = "fixtures/aws/s3/get-bucket-policy-response.json";
    private const string CloudFrontLocator = "fixtures/aws/cloudfront/get-distribution-config-response.json";
    private const string TerraformLocator = "fixtures/terraform/release-storage-resource.json";
    private const string GithubRepositoryLocator = "fixtures/github/repository-response.json";
    private const string GithubCommitLocator = "fixtures/github/commit-response.json";
    private const string CiLocator = "fixtures/ci/workflow-run-response.json";
    private const string AttestationLocator = "fixtures/provenance/sigstore-attestation.json";

    private const string HeadSha = "7a94f3e8b2c1d5f6a7b8c9d0e1f2a3b4c5d6e7f8";

    [Fact]
    public void TheEncryptionAdapterEmitsTheObservedBucketEncryptionConfiguration()
    {
        var state = Normalize(AwsEncryptionLocator);

        var node = Assert.Single(state.Nodes);
        Assert.Equal("aws:s3:prod-release-artifacts", node.Id);
        Assert.Equal("S3Bucket", node.Type.Name);

        var encryption = Assert.Single(node.Properties);
        Assert.Equal("encryption", encryption.Name);
        Assert.Equal("""{"algorithm":"aws:kms","key_arn":"arn:aws:kms:us-east-1:123456789012:key/abcdef12-3456-7890-abcd-ef1234567890","bucket_key_enabled":true}""",
            encryption.Value!.ToJsonString());

        // Observed cloud state, traceable to the mocked API response.
        Assert.Equal(SourceKind.AwsObserved, Assert.Single(node.Provenance.Sources).Kind);
        Assert.Equal(AwsEncryptionLocator, node.Provenance.Sources[0].Locator);
        Assert.Equal(AwsEncryptionLocator, encryption.Provenance.Sources[0].Locator);
    }

    [Fact]
    public void ThePolicyAdapterDerivesTheBucketIdentityFromThePolicyResources()
    {
        var state = Normalize(AwsPolicyLocator);

        var node = Assert.Single(state.Nodes);
        Assert.Equal("aws:s3:prod-release-artifacts", node.Id);

        var policy = node.Properties.Single(property => property.Name == "policy");
        var policyObject = Assert.IsType<JsonObject>(policy.Value);
        Assert.Equal("2012-10-17", policyObject["version"]!.GetValue<string>());
        var statements = Assert.IsType<JsonArray>(policyObject["statements"]);
        Assert.Equal(2, statements.Count);

        var allow = Assert.IsType<JsonObject>(statements[0]);
        Assert.Equal("AllowCloudFrontReadAccess", allow["sid"]!.GetValue<string>());
        Assert.Equal("Allow", allow["effect"]!.GetValue<string>());
        Assert.Equal("s3:GetObject", Assert.IsType<JsonArray>(allow["actions"])[0]!.GetValue<string>());

        var deny = Assert.IsType<JsonObject>(statements[1]);
        Assert.Equal("DenyPublicAccess", deny["sid"]!.GetValue<string>());
        Assert.Equal("Deny", deny["effect"]!.GetValue<string>());
        Assert.Equal("*", deny["principal"]!.GetValue<string>());

        Assert.Equal(AwsPolicyLocator, policy.Provenance.Sources[0].Locator);
    }

    [Fact]
    public void TheCloudFrontAdapterEmitsTheObservedDistributionAndItsOriginEdge()
    {
        var state = Normalize(CloudFrontLocator);

        var node = Assert.Single(state.Nodes);
        Assert.Equal("aws:cloudfront:customer-downloads", node.Id);
        Assert.Equal("CloudFrontDistribution", node.Type.Name);
        Assert.True(node.Properties.Single(property => property.Name == "enabled").Value!.GetValue<bool>());
        Assert.Equal("redirect-to-https", node.Properties.Single(property => property.Name == "viewer_protocol_policy").Value!.GetValue<string>());
        Assert.Equal("TLSv1.2_2021", node.Properties.Single(property => property.Name == "minimum_protocol_version").Value!.GetValue<string>());
        Assert.Equal("downloads.nexusdefend.com", Assert.IsType<JsonArray>(
            node.Properties.Single(property => property.Name == "aliases").Value)![0]!.GetValue<string>());

        // The observed origin: the distribution's default origin is the S3
        // bucket named by its origin domain.
        var edge = Assert.Single(state.Edges);
        Assert.Equal("origin", edge.Name);
        Assert.Equal("aws:cloudfront:customer-downloads", edge.FromId);
        Assert.Equal("aws:s3:prod-release-artifacts", edge.ToId);
        Assert.Equal(SourceKind.AwsObserved, edge.Provenance.Sources[0].Kind);
        Assert.Equal(CloudFrontLocator, edge.Provenance.Sources[0].Locator);
    }

    [Fact]
    public void TheTerraformAdapterEmitsTheAppliedInfrastructureState()
    {
        var state = Normalize(TerraformLocator);

        var node = Assert.Single(state.Nodes);
        Assert.Equal("aws:s3:prod-release-artifacts", node.Id);
        Assert.Equal("S3Bucket", node.Type.Name);

        // Declared-infrastructure facts: what terraform apply recorded, not
        // what a live API observes.
        Assert.Equal("enabled", node.Properties.Single(property => property.Name == "versioning").Value!.GetValue<string>());
        Assert.Equal("""{"algorithm":"aws:kms","key_arn":"arn:aws:kms:us-east-1:123456789012:key/abcdef12-3456-7890-abcd-ef1234567890","bucket_key_enabled":true}""",
            node.Properties.Single(property => property.Name == "encryption").Value!.ToJsonString());
        Assert.True(node.Properties.Single(property => property.Name == "public_access_block").Value!.GetValue<bool>());
        Assert.Equal("production", Assert.IsType<JsonObject>(
            node.Properties.Single(property => property.Name == "tags").Value)!["Environment"]!.GetValue<string>());

        Assert.All(
            node.Properties.SelectMany(property => property.Provenance.Sources).Concat(node.Provenance.Sources),
            record => Assert.Equal(SourceKind.Terraform, record.Kind));
        Assert.All(
            node.Properties.SelectMany(property => property.Provenance.Sources).Concat(node.Provenance.Sources),
            record => Assert.Equal(TerraformLocator, record.Locator));
    }

    [Fact]
    public void TheGithubAdaptersEmitRepositoryAndCommitFacts()
    {
        var repository = Assert.Single(Normalize(GithubRepositoryLocator).Nodes);
        Assert.Equal("repository:customer-agent", repository.Id);
        Assert.Equal("SourceRepository", repository.Type.Name);
        Assert.Equal("rust", repository.Properties.Single(property => property.Name == "language").Value!.GetValue<string>());
        Assert.Equal("main", repository.Properties.Single(property => property.Name == "default_branch").Value!.GetValue<string>());
        Assert.Equal("https://github.com/nexusdefend/customer-agent", repository.Properties.Single(property => property.Name == "url").Value!.GetValue<string>());
        Assert.True(repository.Properties.Single(property => property.Name == "private").Value!.GetValue<bool>());
        Assert.Equal(GithubRepositoryLocator, repository.Provenance.Sources[0].Locator);

        var commit = Assert.Single(Normalize(GithubCommitLocator).Nodes);
        Assert.Equal($"commit:{HeadSha}", commit.Id);
        Assert.Equal("Commit", commit.Type.Name);
        Assert.Equal(HeadSha, commit.Properties.Single(property => property.Name == "sha").Value!.GetValue<string>());
        Assert.Contains("2.8.4", commit.Properties.Single(property => property.Name == "message").Value!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("2026-08-08T13:45:00Z", commit.Properties.Single(property => property.Name == "committed_at").Value!.GetValue<string>());
        Assert.Equal(GithubCommitLocator, commit.Provenance.Sources[0].Locator);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-08T13:45:00Z", CultureInfo.InvariantCulture),
            commit.Provenance.Sources[0].Timestamp);
    }

    [Fact]
    public void TheCiAdapterEmitsTheBuildRunAndItsCommitAndRepositoryEdges()
    {
        var state = Normalize(CiLocator);

        var run = Assert.Single(state.Nodes);
        Assert.Equal("build-run:84125", run.Id);
        Assert.Equal("BuildRun", run.Type.Name);
        Assert.Equal(HeadSha, run.Properties.Single(property => property.Name == "head_sha").Value!.GetValue<string>());
        Assert.Equal("completed", run.Properties.Single(property => property.Name == "status").Value!.GetValue<string>());
        Assert.Equal("success", run.Properties.Single(property => property.Name == "conclusion").Value!.GetValue<string>());
        Assert.Equal(SourceKind.Ci, run.Provenance.Sources[0].Kind);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-08T13:50:00Z", CultureInfo.InvariantCulture),
            run.Provenance.Sources[0].Timestamp);

        var builtFrom = Assert.Single(state.Edges, edge => edge.Name == "built-from");
        Assert.Equal("build-run:84125", builtFrom.FromId);
        Assert.Equal($"commit:{HeadSha}", builtFrom.ToId);

        var committedTo = Assert.Single(state.Edges, edge => edge.Name == "committed-to");
        Assert.Equal($"commit:{HeadSha}", committedTo.FromId);
        Assert.Equal("repository:customer-agent", committedTo.ToId);

        Assert.All(state.Edges, edge => Assert.Equal(CiLocator, edge.Provenance.Sources[0].Locator));
    }

    [Fact]
    public void TheAttestationAdapterEmitsTheDigestAndTheDerivedEdges()
    {
        var state = Normalize(AttestationLocator);

        var artifact = Assert.Single(state.Nodes);
        Assert.Equal("artifact:customer-agent-2.8.4", artifact.Id);
        Assert.Equal("Artifact", artifact.Type.Name);
        var digest = Assert.Single(artifact.Properties);
        Assert.Equal("digest", digest.Name);
        Assert.Equal("sha256:abc123def4567890abcdef1234567890abcdef1234567890abcdef12345678", digest.Value!.GetValue<string>());

        var producedBy = Assert.Single(state.Edges, edge => edge.Name == "produced-by");
        Assert.Equal("artifact:customer-agent-2.8.4", producedBy.FromId);
        Assert.Equal("build-run:84125", producedBy.ToId);
        Assert.Equal(
            [SourceKind.ProvenanceAttestation, SourceKind.Ci],
            producedBy.Provenance.Sources.Select(record => record.Kind).ToArray());
        Assert.Equal(AttestationLocator, producedBy.Provenance.Sources[0].Locator);
        Assert.Equal("slsa-provenance/v1", producedBy.Provenance.Sources[0].Version);
        Assert.Equal(CiLocator, producedBy.Provenance.Sources[1].Locator);

        // The head commit and repository come from the workflow-run document
        // the attestation names by its invocation id.
        var builtFrom = Assert.Single(state.Edges, edge => edge.Name == "built-from");
        Assert.Equal("build-run:84125", builtFrom.FromId);
        Assert.Equal($"commit:{HeadSha}", builtFrom.ToId);
        Assert.Equal(SourceKind.Ci, Assert.Single(builtFrom.Provenance.Sources).Kind);

        var committedTo = Assert.Single(state.Edges, edge => edge.Name == "committed-to");
        Assert.Equal($"commit:{HeadSha}", committedTo.FromId);
        Assert.Equal("repository:customer-agent", committedTo.ToId);
        Assert.Equal(SourceKind.Ci, Assert.Single(committedTo.Provenance.Sources).Kind);
    }

    [Fact]
    public void TheAttestationAdapterEstablishesTheArtifactBuildCommitRepositoryLinkage()
    {
        var world = LoadWorld();
        var graph = EmptyGraph();
        FixtureAdapterRegistry.MockWorld.ApplyAll(graph, world.Fixtures);

        // artifact → build → commit → repository, as typed edges.
        var producedBy = Assert.Single(graph.GetEdges("artifact:customer-agent-2.8.4", "produced-by"));
        Assert.Equal("build-run:84125", producedBy.ToId);
        var builtFrom = Assert.Single(graph.GetEdges("build-run:84125", "built-from"));
        Assert.Equal($"commit:{HeadSha}", builtFrom.ToId);
        var committedTo = Assert.Single(graph.GetEdges($"commit:{HeadSha}", "committed-to"));
        Assert.Equal("repository:customer-agent", committedTo.ToId);

        // The same edges answer from the inverse direction.
        Assert.Same(producedBy, Assert.Single(graph.GetEdges("build-run:84125", "produces")));
        Assert.Same(builtFrom, Assert.Single(graph.GetEdges($"commit:{HeadSha}", "built-into")));
        Assert.Same(committedTo, Assert.Single(graph.GetEdges("repository:customer-agent", "has-commits")));

        // Every hop is resolved, and the artifact→build edge carries the
        // attestation ahead of the CI record — by declared precedence.
        Assert.True(producedBy.IsResolved);
        Assert.Equal(
            [SourceKind.ProvenanceAttestation, SourceKind.Ci],
            producedBy.Provenance.Sources.Select(record => record.Kind).ToArray());
        Assert.Equal(
            SourceKind.ProvenanceAttestation,
            SourceAuthorityRegistry.Slice.MostAuthoritative(ClaimType.Derivation, producedBy.Provenance.Sources)!.Kind);

        // The artifact's observed digest corroborates the declared record's.
        var artifact = graph.GetNode("artifact:customer-agent-2.8.4");
        Assert.Equal("sha256:abc123def4567890abcdef1234567890abcdef1234567890abcdef12345678",
            artifact.Properties["digest"]!.GetValue<string>());
    }

    [Fact]
    public void FactsForTheSameSubjectFromSeveralAdaptersMergeIntoOneNodeWithoutLosingSources()
    {
        var world = LoadWorld();
        var graph = EmptyGraph();
        var registry = FixtureAdapterRegistry.MockWorld;

        // Terraform first, then the two AWS observations: three adapters state
        // facts about the same bucket.
        foreach (var locator in new[] { TerraformLocator, AwsEncryptionLocator, AwsPolicyLocator })
        {
            var document = world.Fixtures.Single(fixture => fixture.RelativePath == locator);
            registry.FindAdapter(document)!.Normalize(document, world.Fixtures).ApplyTo(graph);
        }

        var bucket = Assert.Single(graph.GetNodes(Node("S3Bucket")));
        Assert.Equal("aws:s3:prod-release-artifacts", bucket.Id);
        Assert.Equal(
            ["arn", "encryption", "force_destroy", "policy", "public_access_block", "tags", "versioning"],
            bucket.Properties.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());

        // The bucket's own provenance keeps every source that stated it exists.
        Assert.Equal(
            [SourceKind.Terraform, SourceKind.AwsObserved, SourceKind.AwsObserved],
            bucket.Provenance.Sources.Select(record => record.Kind).ToArray());
        Assert.Equal(
            [TerraformLocator, AwsEncryptionLocator, AwsPolicyLocator],
            bucket.Provenance.Sources.Select(record => record.Locator).ToArray());

        // The encryption property is stated by Terraform and by the AWS
        // observation; the first value stays and both sources are recorded.
        var encryptionProvenance = bucket.GetPropertyProvenance("encryption");
        Assert.Equal(
            [SourceKind.Terraform, SourceKind.AwsObserved],
            encryptionProvenance.Sources.Select(record => record.Kind).ToArray());
    }

    [Fact]
    public void ApplyingTheMockWorldTwiceDoesNotDuplicateNodesEdgesOrProvenance()
    {
        var world = LoadWorld();
        var graph = EmptyGraph();
        FixtureAdapterRegistry.MockWorld.ApplyAll(graph, world.Fixtures);
        FixtureAdapterRegistry.MockWorld.ApplyAll(graph, world.Fixtures);

        Assert.Equal(6, graph.Nodes.Count);
        Assert.Equal(4, graph.Edges.Count);

        var builtFrom = Assert.Single(graph.GetEdges("build-run:84125", "built-from"));
        Assert.Equal(SourceKind.Ci, Assert.Single(builtFrom.Provenance.Sources).Kind);

        var bucket = graph.GetNode("aws:s3:prod-release-artifacts");
        Assert.Equal(3, bucket.Provenance.Sources.Count);
    }

    [Fact]
    public void NormalizingTheWholeMockWorldYieldsTheObservedGraph()
    {
        var world = LoadWorld();
        var graph = EmptyGraph();
        FixtureAdapterRegistry.MockWorld.ApplyAll(graph, world.Fixtures);

        Assert.Equal(
            [
                "artifact:customer-agent-2.8.4",
                "aws:cloudfront:customer-downloads",
                "aws:s3:prod-release-artifacts",
                "build-run:84125",
                "commit:7a94f3e8b2c1d5f6a7b8c9d0e1f2a3b4c5d6e7f8",
                "repository:customer-agent",
            ],
            graph.Nodes.Select(node => node.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray());

        // Every fact in the graph knows where it came from.
        foreach (var node in graph.Nodes)
        {
            Assert.True(node.Provenance.HasKnownSource, $"node '{node.Id}' has no known source");
            Assert.All(node.Properties, pair =>
                Assert.True(node.GetPropertyProvenance(pair.Key).HasKnownSource, $"property '{node.Id}.{pair.Key}' has no known source"));
        }

        Assert.All(graph.Edges, edge => Assert.True(edge.Provenance.HasKnownSource));
        Assert.Empty(graph.GetUnresolvedEdges());
    }

    private static RepositoryData LoadWorld() => RepositoryDataLoader.Load(TestWorld.RepositoryRoot);

    private static NormalizedState Normalize(string relativePath)
    {
        var world = LoadWorld();
        var document = world.Fixtures.Single(fixture => fixture.RelativePath == relativePath);
        var adapter = FixtureAdapterRegistry.MockWorld.FindAdapter(document)
            ?? throw new InvalidOperationException($"No adapter normalizes '{relativePath}'.");
        return adapter.Normalize(document, world.Fixtures);
    }

    private static TypedPropertyGraph EmptyGraph() => new(EdgeTypeRegistry.Slice);

    private static NodeType Node(string name) =>
        EdgeTypeRegistry.Slice.NodeTypes.Single(type => type.Name == name);
}
