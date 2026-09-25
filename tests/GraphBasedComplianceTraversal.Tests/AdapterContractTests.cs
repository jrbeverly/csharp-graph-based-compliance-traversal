using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Adapters;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Pins the adapter contract itself: every emitted fact carries its source's
/// provenance kind and locator, adapters normalize parsed responses without
/// touching files or the network, swapping a fixture for another well-formed
/// response of the same shape changes only the observed facts, and the
/// fixture reader dispatches every fixture to exactly one adapter.
/// </summary>
public sealed class AdapterContractTests
{
    private const string AwsEncryptionLocator = "fixtures/aws/s3/get-bucket-encryption-response.json";
    private const string AwsPolicyLocator = "fixtures/aws/s3/get-bucket-policy-response.json";
    private const string CloudFrontLocator = "fixtures/aws/cloudfront/get-distribution-config-response.json";
    private const string TerraformLocator = "fixtures/terraform/release-storage-resource.json";
    private const string GithubRepositoryLocator = "fixtures/github/repository-response.json";
    private const string GithubCommitLocator = "fixtures/github/commit-response.json";
    private const string CiLocator = "fixtures/ci/workflow-run-response.json";
    private const string AttestationLocator = "fixtures/provenance/sigstore-attestation.json";

    [Theory]
    [InlineData(AwsEncryptionLocator, SourceKind.AwsObserved)]
    [InlineData(AwsPolicyLocator, SourceKind.AwsObserved)]
    [InlineData(CloudFrontLocator, SourceKind.AwsObserved)]
    [InlineData(TerraformLocator, SourceKind.Terraform)]
    [InlineData(GithubRepositoryLocator, SourceKind.Github)]
    [InlineData(GithubCommitLocator, SourceKind.Github)]
    [InlineData(CiLocator, SourceKind.Ci)]
    public void EveryFactAnAdapterEmitsCarriesItsSourceKindAndLocator(string relativePath, SourceKind expectedKind)
    {
        var world = LoadWorld();
        var document = world.Fixtures.Single(fixture => fixture.RelativePath == relativePath);
        var adapter = FixtureAdapterRegistry.MockWorld.FindAdapter(document)!;
        var state = adapter.Normalize(document, world.Fixtures);

        Assert.True(state.Nodes.Count + state.Edges.Count > 0, "the adapter emitted no facts.");

        var records = state.Nodes
            .SelectMany(node => node.Provenance.Sources.Concat(node.Properties.SelectMany(property => property.Provenance.Sources)))
            .Concat(state.Edges.SelectMany(edge => edge.Provenance.Sources))
            .ToArray();

        Assert.NotEmpty(records);
        Assert.All(records, record => Assert.Equal(expectedKind, record.Kind));
        Assert.All(records, record => Assert.Equal(relativePath, record.Locator));
    }

    [Fact]
    public void TheAttestationFactsCarryTheAttestationAndCiRecordsWithTheirLocators()
    {
        var world = LoadWorld();
        var document = world.Fixtures.Single(fixture => fixture.RelativePath == AttestationLocator);
        var adapter = FixtureAdapterRegistry.MockWorld.FindAdapter(document)!;
        var state = adapter.Normalize(document, world.Fixtures);

        var records = state.Nodes
            .SelectMany(node => node.Provenance.Sources.Concat(node.Properties.SelectMany(property => property.Provenance.Sources)))
            .Concat(state.Edges.SelectMany(edge => edge.Provenance.Sources))
            .ToArray();

        Assert.Contains(records, record =>
            record.Kind == SourceKind.ProvenanceAttestation && record.Locator == AttestationLocator);
        Assert.Contains(records, record => record.Kind == SourceKind.Ci && record.Locator == CiLocator);
        Assert.All(records, record => Assert.Equal(
            (record.Kind == SourceKind.ProvenanceAttestation ? AttestationLocator : CiLocator),
            record.Locator));
    }

    [Fact]
    public void SwappingAFixtureForAnotherResponseOfTheSameShapeChangesOnlyTheObservedFacts()
    {
        var world = LoadWorld();
        var registry = FixtureAdapterRegistry.MockWorld;

        var original = EmptyGraph();
        registry.ApplyAll(original, world.Fixtures);

        // A second encryption response of the same shape: same API, same
        // locator, different configured algorithm (AES256 instead of aws:kms).
        var swappedContent = JsonNode.Parse("""
            {
              "$description": "Swapped mock response of the same shape.",
              "ServerSideEncryptionConfiguration": {
                "Rules": [
                  {
                    "ApplyServerSideEncryptionByDefault": { "SSEAlgorithm": "AES256" },
                    "BucketKeyEnabled": false
                  }
                ]
              }
            }
            """);
        var swappedFixtures = world.Fixtures
            .Select(fixture => fixture.RelativePath == AwsEncryptionLocator
                ? new RepositoryDocument(fixture.RelativePath, fixture.Format, fixture.Identifier, swappedContent!)
                : fixture)
            .ToArray();

        // The same registry and the same adapter instances normalize both
        // worlds — swapping the fixture changes no downstream code path.
        Assert.Same(
            registry.FindAdapter(world.Fixtures.Single(fixture => fixture.RelativePath == AwsEncryptionLocator)),
            registry.FindAdapter(swappedFixtures.Single(fixture => fixture.RelativePath == AwsEncryptionLocator)));

        var swapped = EmptyGraph();
        registry.ApplyAll(swapped, swappedFixtures);

        // Only the observed encryption fact changed; every other node, edge,
        // and property is identical.
        Assert.Equal(
            original.Nodes.Select(node => node.Id),
            swapped.Nodes.Select(node => node.Id));
        Assert.Equal(
            original.Edges.Select(edge => (edge.Name, edge.FromId, edge.ToId)),
            swapped.Edges.Select(edge => (edge.Name, edge.FromId, edge.ToId)));

        foreach (var node in original.Nodes)
        {
            var other = swapped.GetNode(node.Id);
            Assert.Equal(node.Properties.Keys.OrderBy(name => name, StringComparer.Ordinal),
                other.Properties.Keys.OrderBy(name => name, StringComparer.Ordinal));
            foreach (var (property, value) in node.Properties)
            {
                if (node.Id == "aws:s3:prod-release-artifacts" && property == "encryption")
                {
                    continue;
                }

                Assert.True(JsonNode.DeepEquals(value, other.Properties[property]), $"{node.Id}.{property} changed.");
            }
        }

        var swappedEncryption = swapped.GetNode("aws:s3:prod-release-artifacts").Properties["encryption"];
        Assert.Equal("""{"algorithm":"AES256","bucket_key_enabled":false}""", swappedEncryption!.ToJsonString());
    }

    [Fact]
    public void AdaptersNormalizeParsedResponsesWithoutReadingFiles()
    {
        // The locator names a file that does not exist on disk. The adapter
        // works from the parsed content alone — proof that it performs no
        // file or network access and requires no credentials.
        var content = JsonNode.Parse("""
            {
              "ServerSideEncryptionConfiguration": {
                "Rules": [
                  {
                    "ApplyServerSideEncryptionByDefault": {
                      "SSEAlgorithm": "aws:kms",
                      "KMSMasterKeyID": "arn:aws:kms:us-east-1:123456789012:key/abcdef12-3456-7890-abcd-ef1234567890"
                    },
                    "BucketKeyEnabled": true
                  }
                ]
              }
            }
            """);
        var document = new RepositoryDocument(
            "fixtures/aws/s3/no-such-file-on-disk.json",
            RepositoryDocumentFormat.Json,
            null,
            content!);

        var adapter = FixtureAdapterRegistry.MockWorld.FindAdapter(document)!;
        var state = adapter.Normalize(document, []);

        var node = Assert.Single(state.Nodes);
        Assert.Equal("aws:s3:prod-release-artifacts", node.Id);
        Assert.Equal("fixtures/aws/s3/no-such-file-on-disk.json", node.Provenance.Sources[0].Locator);
    }

    [Fact]
    public void EveryRepositoryFixtureIsDispatchedToExactlyOneAdapter()
    {
        var world = LoadWorld();
        var registry = FixtureAdapterRegistry.MockWorld;

        Assert.Equal(8, world.Fixtures.Count);
        foreach (var fixture in world.Fixtures)
        {
            var adapter = registry.FindAdapter(fixture);
            Assert.True(adapter is not null, $"no adapter normalizes '{fixture.RelativePath}'.");
        }

        Assert.Equal(8, registry.Adapters.Select(adapter => adapter.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(8, registry.NormalizeAll(world.Fixtures).Count);
    }

    [Fact]
    public void AResponseNoAdapterHandlesIsRejectedRatherThanSilentlySkipped()
    {
        var document = new RepositoryDocument(
            "fixtures/aws/s3/unknown-response.json",
            RepositoryDocumentFormat.Json,
            null,
            JsonNode.Parse("""{"UnknownField": "no adapter matches this shape"}""")!);

        var exception = Assert.Throws<AdapterException>(
            () => FixtureAdapterRegistry.MockWorld.NormalizeAll([document]));

        Assert.Equal("fixtures/aws/s3/unknown-response.json", exception.Path);
        Assert.Contains("no adapter normalizes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAttestationAdapterRequiresTheWorkflowRunItReferences()
    {
        var world = LoadWorld();
        var attestation = world.Fixtures.Single(fixture => fixture.RelativePath == AttestationLocator);
        var adapter = FixtureAdapterRegistry.MockWorld.FindAdapter(attestation)!;

        // Without the workflow-run document the invocation id names, the
        // linkage cannot be established — and the adapter says so.
        var exception = Assert.Throws<AdapterException>(() => adapter.Normalize(attestation, []));

        Assert.Contains("84125", exception.Message, StringComparison.Ordinal);
        Assert.Equal(AttestationLocator, exception.Path);
    }

    private static RepositoryData LoadWorld() => RepositoryDataLoader.Load(TestWorld.RepositoryRoot);

    private static TypedPropertyGraph EmptyGraph() => new(EdgeTypeRegistry.Slice);
}
