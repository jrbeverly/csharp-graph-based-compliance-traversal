using System.Globalization;
using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Assertions;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ingestion;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;
using GraphBasedComplianceTraversal.Engine.State;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Pins the assertion milestone: the slice's three assertions evaluate
/// deterministically against the graph, one observation per subject, with a
/// shared-model state decided from the subject's facts, the evaluation
/// timestamp, and the provenance of the facts consulted. States are honest —
/// a subject with no supporting fact resolves Unknown, never an implied pass.
/// The scenarios run against the repository's own data world and against
/// small synthetic worlds for the failure and unknown paths.
/// </summary>
public sealed class AssertionEvaluationTests
{
    private const string ArtifactDeclarationLocator = "data/technical/artifacts/customer-agent-2.8.4.yaml";
    private const string DistributionDeclarationLocator = "data/cloud/aws/resources/cloudfront-customer-downloads.yaml";
    private const string BucketDeclarationLocator = "data/cloud/aws/resources/s3-prod-release-artifacts.yaml";
    private const string AdoptionDeclarationLocator = "data/security/controls/adoptions/ca-artifact-supply-chain.yaml";

    private static readonly DateTimeOffset EvaluatedAt =
        DateTimeOffset.Parse("2026-08-12T09:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void EvaluatingProductionArtifactProvenanceOverTheLoadedGraphYieldsObservationsMatchingTheFacts()
    {
        var graph = LoadDeclaredGraph();
        var assertion = AssertionRegistry.Slice.Require("assertion:prod-artifact-provenance");

        var observations = assertion.Evaluate(graph, EvaluatedAt);

        // The world carries one production artifact, and its record declares
        // the complete signing fact set, so the observation passes — with the
        // evaluation timestamp and the provenance of the signing facts it
        // consulted.
        var observation = Assert.Single(observations);
        Assert.Equal("assertion:prod-artifact-provenance", observation.AssertionId);
        Assert.Equal("artifact:customer-agent-2.8.4", observation.SubjectId);
        Assert.Equal(ValidationState.Pass, observation.State);
        Assert.Equal(EvaluatedAt, observation.EvaluatedAt);
        Assert.Contains("signing", observation.Reason, StringComparison.Ordinal);

        var source = Assert.Single(observation.Provenance.Sources);
        Assert.Equal(SourceKind.Declaration, source.Kind);
        Assert.Equal(ArtifactDeclarationLocator, source.Locator);
    }

    [Fact]
    public void ASubjectWithNoSupportingFactYieldsUnknownNeverAnImpliedPass()
    {
        var graph = EmptyGraph();
        graph.AddNode(
            "artifact:unsupported",
            Node("Artifact"),
            new Dictionary<string, JsonNode?>
            {
                ["classification"] = JsonValue.Create("production-release"),
            });
        graph.AddNode(
            "aws:s3:ownerless",
            Node("S3Bucket"),
            new Dictionary<string, JsonNode?>
            {
                ["classification"] = JsonValue.Create("production"),
            });
        graph.AddNode("control-adoption:unimplemented", Node("ControlAdoption"));

        var observations = AssertionRegistry.Slice.EvaluateAll(graph, EvaluatedAt);

        // One observation per subject, in assertion then selector order; each
        // subject carries no fact supporting its assertion, so each resolves
        // Unknown — absence of a supporting fact is never promoted to a pass.
        Assert.Equal(
            ["artifact:unsupported", "aws:s3:ownerless", "control-adoption:unimplemented"],
            observations.Select(observation => observation.SubjectId).ToArray());
        Assert.All(observations, observation =>
        {
            Assert.Equal(ValidationState.Unknown, observation.State);
            Assert.NotEqual(ValidationState.Pass, observation.State);
            Assert.NotEmpty(observation.Reason);
        });
    }

    [Fact]
    public void OwnerlessProductionResourcesAreUnknownNeverPassedByOmission()
    {
        var graph = LoadDeclaredGraph();
        var assertion = AssertionRegistry.Slice.Require("assertion:production-resource-owner");

        var observations = assertion.Evaluate(graph, EvaluatedAt);

        // Both production cloud resources carry no owner fact, so neither
        // passes by omission: each resolves Unknown instead.
        Assert.Equal(
            ["aws:cloudfront:customer-downloads", "aws:s3:prod-release-artifacts"],
            observations.Select(observation => observation.SubjectId).ToArray());
        Assert.All(observations, observation =>
        {
            Assert.Equal(ValidationState.Unknown, observation.State);
            Assert.NotEqual(ValidationState.Pass, observation.State);
            Assert.Contains("no owner", observation.Reason, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void AnOwnerFactPassesTheResourceAndAnEmptyOneFails()
    {
        var graph = EmptyGraph();
        graph.AddNode(
            "aws:s3:owned",
            Node("S3Bucket"),
            new Dictionary<string, JsonNode?>
            {
                ["classification"] = JsonValue.Create("production"),
                ["owner"] = JsonValue.Create("team:platform-engineering"),
            });
        graph.AddNode(
            "aws:s3:owner-empty",
            Node("S3Bucket"),
            new Dictionary<string, JsonNode?>
            {
                ["classification"] = JsonValue.Create("production"),
                ["owner"] = JsonValue.Create(string.Empty),
            });

        var assertion = AssertionRegistry.Slice.Require("assertion:production-resource-owner");
        var observations = assertion.Evaluate(graph, EvaluatedAt);

        var owned = Assert.Single(observations, observation => observation.SubjectId == "aws:s3:owned");
        Assert.Equal(ValidationState.Pass, owned.State);
        Assert.Contains("team:platform-engineering", owned.Reason, StringComparison.Ordinal);

        var empty = Assert.Single(observations, observation => observation.SubjectId == "aws:s3:owner-empty");
        Assert.Equal(ValidationState.Fail, empty.State);
        Assert.Contains("empty", empty.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AProductionArtifactWithIncompleteSigningFactsFails()
    {
        var graph = EmptyGraph();
        graph.AddNode(
            "artifact:partially-signed",
            Node("Artifact"),
            new Dictionary<string, JsonNode?>
            {
                ["classification"] = JsonValue.Create("production-release"),
                ["signing"] = JsonNode.Parse("""{"method":"sigstore"}"""),
            });

        var assertion = AssertionRegistry.Slice.Require("assertion:prod-artifact-provenance");
        var observation = Assert.Single(assertion.Evaluate(graph, EvaluatedAt));

        Assert.Equal(ValidationState.Fail, observation.State);
        Assert.Contains("'identity'", observation.Reason, StringComparison.Ordinal);
        Assert.Contains("'transparency_log'", observation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAdoptedControlPassesItsImplementationCheckOverTheLoadedGraph()
    {
        var graph = LoadDeclaredGraph();
        var assertion = AssertionRegistry.Slice.Require("assertion:adopted-control-implementation");

        var observation = Assert.Single(assertion.Evaluate(graph, EvaluatedAt));

        Assert.Equal("control-adoption:ca-artifact-supply-chain", observation.SubjectId);
        Assert.Equal(ValidationState.Pass, observation.State);
        Assert.Contains("implementation:signed-release-pipeline", observation.Reason, StringComparison.Ordinal);

        // The facts consulted are the adoption's implemented-by edge, stated
        // by the adoption record and restated by the implementation record.
        Assert.Equal(2, observation.Provenance.Sources.Count);
        Assert.All(observation.Provenance.Sources, source => Assert.Equal(SourceKind.Declaration, source.Kind));
        Assert.Contains(observation.Provenance.Sources, source => source.Locator == AdoptionDeclarationLocator);
    }

    [Fact]
    public void AnAdoptionDeclaringAnImplementationWithoutARecordFails()
    {
        var graph = EmptyGraph();
        graph.AddNode("control-adoption:broken", Node("ControlAdoption"));
        graph.AddEdge(
            "implemented-by",
            "control-adoption:broken",
            Node("ControlAdoption"),
            "implementation:missing",
            Node("ControlImplementation"));

        var assertion = AssertionRegistry.Slice.Require("assertion:adopted-control-implementation");
        var observation = Assert.Single(assertion.Evaluate(graph, EvaluatedAt));

        Assert.Equal(ValidationState.Fail, observation.State);
        Assert.Contains("implementation:missing", observation.Reason, StringComparison.Ordinal);
        Assert.Contains("no record", observation.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonProductionResourceIsOutOfTheOwnerAssertionsScope()
    {
        var graph = EmptyGraph();
        graph.AddNode(
            "aws:s3:staging",
            Node("S3Bucket"),
            new Dictionary<string, JsonNode?>
            {
                ["classification"] = JsonValue.Create("staging"),
            });

        var assertion = AssertionRegistry.Slice.Require("assertion:production-resource-owner");

        Assert.Empty(assertion.Evaluate(graph, EvaluatedAt));
    }

    [Fact]
    public void EveryObservationRecordsTheProvenanceOfTheFactsEvaluated()
    {
        var graph = LoadDeclaredGraph();

        var observations = AssertionRegistry.Slice.EvaluateAll(graph, EvaluatedAt);

        // Four subjects across the three assertions: the production artifact,
        // the two production resources, and the adopted control. Every
        // observation carries the provenance of the facts it evaluated — the
        // declaration record that stated them.
        Assert.Equal(4, observations.Count);
        Assert.All(observations, observation =>
        {
            Assert.True(observation.Provenance.HasKnownSource);
            Assert.All(observation.Provenance.Sources, source =>
            {
                Assert.Equal(SourceKind.Declaration, source.Kind);
                Assert.StartsWith("data/", source.Locator, StringComparison.Ordinal);
            });
        });

        Assert.Equal(ArtifactDeclarationLocator, observations[0].Provenance.Sources.Single().Locator);
        Assert.Equal(DistributionDeclarationLocator, observations[1].Provenance.Sources.Single().Locator);
        Assert.Equal(BucketDeclarationLocator, observations[2].Provenance.Sources.Single().Locator);
        Assert.Contains(observations[3].Provenance.Sources, source => source.Locator == AdoptionDeclarationLocator);
    }

    [Fact]
    public void EvaluationIsDeterministic()
    {
        var graph = LoadDeclaredGraph();

        var first = AssertionRegistry.Slice.EvaluateAll(graph, EvaluatedAt);
        var second = AssertionRegistry.Slice.EvaluateAll(graph, EvaluatedAt);

        Assert.Equal(
            first.Select(observation => (observation.AssertionId, observation.SubjectId, observation.State, observation.EvaluatedAt, observation.Reason)).ToArray(),
            second.Select(observation => (observation.AssertionId, observation.SubjectId, observation.State, observation.EvaluatedAt, observation.Reason)).ToArray());
        Assert.Equal(
            first.Select(observation => observation.Provenance.Sources.Select(source => (source.Kind, source.Locator, source.Timestamp, source.Version)).ToArray()).ToArray(),
            second.Select(observation => observation.Provenance.Sources.Select(source => (source.Kind, source.Locator, source.Timestamp, source.Version)).ToArray()).ToArray());
    }

    [Fact]
    public void TheRegistryRejectsDuplicateAssertionIds()
    {
        var assertion = new Assertion(
            "assertion:duplicate",
            "Duplicate.",
            new QuerySubjectSelector(_ => []),
            new FixedPredicate(new AssertionEvaluation(ValidationState.Unknown, FactProvenance.None, "no facts")));

        var exception = Assert.Throws<ArgumentException>(() => new AssertionRegistry([assertion, assertion]));

        Assert.Contains("registered more than once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequireThrowsForAnUnknownAssertionId()
    {
        var exception = Assert.Throws<KeyNotFoundException>(() => AssertionRegistry.Slice.Require("assertion:missing"));

        Assert.Contains("assertion:missing", exception.Message, StringComparison.Ordinal);
    }

    private static TypedPropertyGraph EmptyGraph() => new(EdgeTypeRegistry.Slice);

    private static TypedPropertyGraph LoadDeclaredGraph()
    {
        var graph = EmptyGraph();
        DeclaredRecordsLoader.ApplyAll(graph, RepositoryDataLoader.Load(TestWorld.RepositoryRoot).Records);
        return graph;
    }

    private static NodeType Node(string name) =>
        EdgeTypeRegistry.Slice.NodeTypes.Single(type => type.Name == name);

    private sealed class FixedPredicate(AssertionEvaluation evaluation) : IAssertionPredicate
    {
        public AssertionEvaluation Evaluate(TypedPropertyGraph graph, GraphNode subject) => evaluation;
    }
}
