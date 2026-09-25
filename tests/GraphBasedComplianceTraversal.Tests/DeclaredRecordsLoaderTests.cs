using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ingestion;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Pins the declared-world loader: every <c>data/**</c> record becomes exactly
/// one typed node carrying its properties and declaration provenance, its
/// reference fields become typed edges in the registry's declared direction,
/// the observations history becomes Observation nodes linked to their
/// assertion, and references with no target record are retained rather than
/// dropped. The scenarios run against the repository's own data world.
/// </summary>
public sealed class DeclaredRecordsLoaderTests
{
    private const string ObservationsLocator = "data/security/observations/provenance-observations.yaml";

    private static readonly string[] ObservationIds =
    [
        "observation:obs-2026-08-08-001",
        "observation:obs-2026-08-07-001",
        "observation:obs-2026-08-06-001",
        "observation:obs-2026-08-05-001",
        "observation:obs-2026-07-15-001",
    ];

    [Fact]
    public void EveryDataRecordBecomesExactlyOneNodeWithItsTypeAndDeclarationProvenance()
    {
        var world = LoadWorld();
        var graph = LoadDeclaredGraph();

        // 21 documents: 20 typed records plus the observations history, whose
        // 5 entries each become one Observation node.
        Assert.Equal(25, graph.Nodes.Count);
        Assert.Equal(5, graph.GetNodes(Node("Observation")).Count);

        foreach (var record in world.Records.Where(record => record.Identifier is not null))
        {
            var node = graph.GetNode(record.Identifier!);
            Assert.Equal(record.Content!["type"]!.GetValue<string>(), node.Type.Name);
            AssertDeclarationProvenance(node.Provenance, record.RelativePath);
        }

        // Every declared fact — node, edge, and property — answers where it
        // came from: the record's file, as a declaration.
        Assert.All(graph.Nodes, node => Assert.All(node.Properties, pair =>
            Assert.True(node.GetPropertyProvenance(pair.Key).HasKnownSource, $"property '{node.Id}.{pair.Key}' has no known source")));
        Assert.All(graph.Edges, edge => Assert.True(edge.Provenance.HasKnownSource));
    }

    [Fact]
    public void TheObservationsHistoryBecomesObservationNodesLinkedToTheirAssertion()
    {
        var graph = LoadDeclaredGraph();
        var assertion = graph.GetNode("assertion:prod-artifact-provenance");

        Assert.Equal(ObservationIds, graph.GetNodes(Node("Observation")).Select(node => node.Id).ToArray());
        Assert.All(graph.GetNodes(Node("Observation")), node => AssertDeclarationProvenance(node.Provenance, ObservationsLocator));

        // Each entry linked to its assertion through the 'produces' edge type,
        // stored under its inverse name because the record's field reads
        // `assertion:`.
        var produces = graph.GetEdges(assertion.Id, "produces");
        Assert.Equal(ObservationIds, produces.Select(edge => edge.FromId).ToArray());
        Assert.All(produces, edge =>
        {
            Assert.Equal(assertion.Id, edge.ToId);
            Assert.True(edge.IsResolved);
            AssertDeclarationProvenance(edge.Provenance, ObservationsLocator);
        });

        // The same edges answer from the observation side, and every entry
        // carries its history properties.
        var latest = graph.GetNode("observation:obs-2026-08-08-001");
        Assert.Same(produces[0], Assert.Single(graph.GetEdges(latest.Id, "produced-by")));
        Assert.Equal("pass", latest.Properties["result"]!.GetValue<string>());
        Assert.Equal("provenance-prober@3.1.7", latest.Properties["validator_version"]!.GetValue<string>());
        Assert.Equal("sha256:abc123def4567890abcdef1234567890abcdef1234567890abcdef12345678",
            Assert.IsType<JsonObject>(latest.Properties["subject"])["digest"]!.GetValue<string>());

        // The failure entry keeps its incident reference as an unmodeled one.
        var failure = graph.GetNode("observation:obs-2026-08-06-001");
        Assert.Equal("fail", failure.Properties["result"]!.GetValue<string>());
        Assert.Equal(
            "INC-2026-041",
            Assert.Single(graph.GetUnmodeledReferences(failure.Id), reference => reference.FieldPath == "related_incident").TargetId);
    }

    [Fact]
    public void TheBusinessToInfrastructureChainIsConnectedAsTypedEdges()
    {
        var graph = LoadDeclaredGraph();

        Assert.Equal(
            ["product:customer-agent"],
            Walk(graph, "business-purpose:secure-endpoint-management", "fulfilled-by"));
        Assert.Equal(
            ["service:release-distribution"],
            Walk(graph, "capability:software-update-delivery", "implemented-by"));
        Assert.Equal(
            ["aws:cloudfront:customer-downloads", "aws:s3:prod-release-artifacts"],
            Walk(graph, "service:release-distribution", "depends-on"));
        Assert.Equal(
            ["aws:s3:prod-release-artifacts"],
            Walk(graph, "aws:cloudfront:customer-downloads", "origin"));
        Assert.Equal(
            ["artifact:customer-agent-2.8.4"],
            Walk(graph, "aws:s3:prod-release-artifacts", "contains"));
        Assert.Equal(
            ["build-pipeline:customer-agent-release"],
            Walk(graph, "artifact:customer-agent-2.8.4", "produced-by"));
        Assert.Equal(
            ["repository:customer-agent"],
            Walk(graph, "build-pipeline:customer-agent-release", "source"));

        // The product exposes three capabilities; the canonical chain takes
        // the delivery one, and the two without records stay visible.
        Assert.Contains("capability:software-update-delivery", Walk(graph, "product:customer-agent", "has-capability"));
        Assert.Equal(
            ["capability:endpoint-telemetry-collection", "capability:policy-enforcement"],
            graph.GetEdges("product:customer-agent", "has-capability")
                .Where(edge => !edge.IsResolved)
                .Select(edge => edge.ToId)
                .ToArray());
    }

    [Fact]
    public void TheRiskChainIsConnectedFromRiskThroughToItsObservations()
    {
        var graph = LoadDeclaredGraph();

        Assert.Equal(
            ["impact:artifact-compromise"],
            Walk(graph, "risk:artifact-tampering", "results-in"));
        Assert.Equal(
            ["control-adoption:ca-artifact-supply-chain"],
            Walk(graph, "impact:artifact-compromise", "informs"));
        Assert.Equal(
            ["implementation:signed-release-pipeline"],
            Walk(graph, "control-adoption:ca-artifact-supply-chain", "implemented-by"));
        Assert.Equal(
            ["assertion:prod-artifact-provenance"],
            Walk(graph, "implementation:signed-release-pipeline", "validated-by"));
        Assert.Equal(
            ObservationIds,
            Walk(graph, "assertion:prod-artifact-provenance", "produces"));

        // Every hop of the chain is resolved, in both directions.
        Assert.Equal(["assertion:prod-artifact-provenance"], Walk(graph, ObservationIds[0], "produced-by"));
        Assert.Equal(["control-adoption:ca-artifact-supply-chain"], Walk(graph, "implementation:signed-release-pipeline", "implements"));
        Assert.All(
            graph.GetEdges("assertion:prod-artifact-provenance", "produces"),
            edge => Assert.True(edge.IsResolved));
    }

    [Fact]
    public void TheComplianceChainAndTheControlDerivationAreConnected()
    {
        var graph = LoadDeclaredGraph();

        Assert.Equal(
            ["requirement:nis2-article-21-supply-chain"],
            Walk(graph, "framework:nis2", "requires"));
        Assert.Equal(
            ["control-adoption:ca-artifact-supply-chain"],
            Walk(graph, "requirement:nis2-article-21-supply-chain", "satisfied-by"));
        Assert.Equal(
            ["requirement:nis2-article-21-supply-chain"],
            Walk(graph, "control-adoption:ca-artifact-supply-chain", "satisfies"));

        // The adoption's bare specification name resolves to the control
        // definition's node id.
        Assert.Equal(
            ["control-def:ARR-3-11"],
            Walk(graph, "control-adoption:ca-artifact-supply-chain", "derived-from"));
        Assert.Equal(
            ["control-adoption:ca-artifact-supply-chain"],
            Walk(graph, "control-def:ARR-3-11", "adopted-by"));
    }

    [Fact]
    public void UnresolvedReferencesArePresentAsUnresolvedRatherThanDropped()
    {
        var graph = LoadDeclaredGraph();

        // One edge per declared relationship; the six whose targets have no
        // record are retained as unresolved edges.
        Assert.Equal(42, graph.Edges.Count);
        Assert.Equal(6, graph.GetUnresolvedEdges().Count);
        Assert.All(graph.Edges, edge => Assert.All(edge.Provenance.Sources, source =>
        {
            // A relationship stated from both sides keeps one record per side.
            Assert.Equal(SourceKind.Declaration, source.Kind);
            Assert.StartsWith("data/", source.Locator, StringComparison.Ordinal);
        }));
        foreach (var missingId in new[]
        {
            "capability:endpoint-telemetry-collection",
            "capability:policy-enforcement",
            "artifact:customer-agent-2.8.3",
            "artifact:customer-agent-2.8.4-unsigned",
            "artifact:customer-agent-2.8.2",
            "artifact:customer-agent-2.8.1",
        })
        {
            Assert.Contains(graph.GetUnresolvedEdges(), edge => edge.MissingEndpointIds.Contains(missingId));
        }

        // References through fields the ontology declares unmodeled are
        // recorded, never dropped.
        Assert.Equal(29, graph.UnmodeledReferences.Count);
        AssertUnmodeledReference(graph, "risk:artifact-tampering", "generic_risk", "ARR-RISK-044");
        AssertUnmodeledReference(graph, "control-adoption:ca-artifact-supply-chain", "approver", "role:CTO");
        AssertUnmodeledReference(graph, "exception:emergency-deployment-path", "approved_by", "role:CTO");
        AssertUnmodeledReference(graph, "repository:customer-agent", "owner", "team:platform-engineering");
        AssertUnmodeledReference(graph, "service:release-distribution", "owned_by", "team:platform-engineering");
    }

    [Fact]
    public void ARecordWithAnUndeclaredNodeTypeIsRejectedWithItsPath()
    {
        var graph = EmptyGraph();
        var record = Document("data/mystery.yaml", """{"id":"mystery:thing","type":"Mystery"}""");

        var exception = Assert.Throws<DeclaredRecordException>(() => DeclaredRecordsLoader.ApplyAll(graph, [record]));

        Assert.Equal("data/mystery.yaml", exception.Path);
        Assert.Contains("'Mystery' is not declared", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordWhoseIdLacksItsTypesPrefixIsRejected()
    {
        var graph = EmptyGraph();
        var record = Document("data/product.yaml", """{"id":"product:customer-agent","type":"Service"}""");

        var exception = Assert.Throws<DeclaredRecordException>(() => DeclaredRecordsLoader.ApplyAll(graph, [record]));

        Assert.Contains("does not carry the 'service' prefix", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordWithNeitherTypedShapeNorObservationsListIsRejected()
    {
        var graph = EmptyGraph();
        var record = Document("data/prose.yaml", """{"summary":"free text"}""");

        var exception = Assert.Throws<DeclaredRecordException>(() => DeclaredRecordsLoader.ApplyAll(graph, [record]));

        Assert.Contains("must declare", exception.Message, StringComparison.Ordinal);
    }

    private static RepositoryData LoadWorld() => RepositoryDataLoader.Load(TestWorld.RepositoryRoot);

    private static TypedPropertyGraph EmptyGraph() => new(EdgeTypeRegistry.Slice);

    private static TypedPropertyGraph LoadDeclaredGraph()
    {
        var graph = EmptyGraph();
        DeclaredRecordsLoader.ApplyAll(graph, LoadWorld().Records);
        return graph;
    }

    private static NodeType Node(string name) =>
        EdgeTypeRegistry.Slice.NodeTypes.Single(type => type.Name == name);

    private static RepositoryDocument Document(string relativePath, string json) =>
        new(relativePath, RepositoryDocumentFormat.Yaml, null, JsonNode.Parse(json)!);

    private static string[] Walk(TypedPropertyGraph graph, string fromId, string name) =>
        graph.GetEdges(fromId, name)
            .Select(edge => edge.ToId == fromId ? edge.FromId : edge.ToId)
            .ToArray();

    private static void AssertDeclarationProvenance(FactProvenance provenance, string locator)
    {
        var record = Assert.Single(provenance.Sources);
        Assert.Equal(SourceKind.Declaration, record.Kind);
        Assert.Equal(locator, record.Locator);
    }

    private static void AssertUnmodeledReference(TypedPropertyGraph graph, string fromId, string fieldPath, string targetId)
    {
        Assert.Contains(
            graph.GetUnmodeledReferences(fromId),
            reference => reference.FieldPath == fieldPath && reference.TargetId == targetId);
    }
}
