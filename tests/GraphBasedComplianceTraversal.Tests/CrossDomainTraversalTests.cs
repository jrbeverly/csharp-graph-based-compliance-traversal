using GraphBasedComplianceTraversal.Engine.Adapters;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ingestion;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;
using GraphBasedComplianceTraversal.Engine.Traversal;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Pins the three canonical cross-domain traversals — architecture, security,
/// and risk as different paths through one shared graph — each covered by a
/// golden explained-path rendering, plus the reverse capability traversal, the
/// typed-edge justification between adjacent nodes, and the unresolved-reference
/// gap reporting. The scenarios run against the shared graph: the repository's
/// declared <c>data/**</c> world merged with the observed <c>fixtures/**</c>
/// world.
/// </summary>
public sealed class CrossDomainTraversalTests
{
    private const string AttestationLocator = "fixtures/provenance/sigstore-attestation.json";
    private const string CiLocator = "fixtures/ci/workflow-run-response.json";
    private const string CommitId = "commit:7a94f3e8b2c1d5f6a7b8c9d0e1f2a3b4c5d6e7f8";

    private static readonly string[] ObservationIds =
    [
        "observation:obs-2026-08-08-001",
        "observation:obs-2026-08-07-001",
        "observation:obs-2026-08-06-001",
        "observation:obs-2026-08-05-001",
        "observation:obs-2026-07-15-001",
    ];

    [Fact]
    public void TheResourceToPurposeTraversalReturnsTheExpectedNodeSequence()
    {
        var graph = LoadSharedGraph();

        var report = CanonicalTraversals.ResourceToPurpose(graph).ExecuteWithGaps();

        // Why this resource exists: up through service, capability, and
        // product to the business purpose it serves.
        var path = Assert.Single(report.Paths);
        Assert.False(path.IsCycle);
        Assert.Equal(
            [
                "aws:s3:prod-release-artifacts",
                "service:release-distribution",
                "capability:software-update-delivery",
                "product:customer-agent",
                "business-purpose:secure-endpoint-management",
            ],
            path.Nodes.Select(node => node.Id).ToArray());
        Assert.Equal(
            ["used-by", "implements", "belongs-to", "fulfills"],
            path.Steps.Select(step => step.Name).ToArray());

        // No unresolved reference on this chain.
        Assert.Empty(report.UnresolvedReferences);

        Golden.AssertMatches("cross-domain/resource-to-purpose.txt", ExplainedPath.Render(report));
    }

    [Fact]
    public void TheReverseTraversalFromAPurposeEnumeratesTheTechnicalSystemsAndReportsTheGaps()
    {
        var graph = LoadSharedGraph();

        var report = CanonicalTraversals.PurposeToTechnicalSystems(graph).ExecuteWithGaps();

        // What business outcome is affected if a resource fails: the purpose
        // down through product, capability, and service, fanning out over the
        // service's two technical dependencies in record order.
        Assert.Equal(
            [
                [
                    "business-purpose:secure-endpoint-management",
                    "product:customer-agent",
                    "capability:software-update-delivery",
                    "service:release-distribution",
                    "aws:cloudfront:customer-downloads",
                ],
                [
                    "business-purpose:secure-endpoint-management",
                    "product:customer-agent",
                    "capability:software-update-delivery",
                    "service:release-distribution",
                    "aws:s3:prod-release-artifacts",
                ],
            ],
            report.Paths.Select(path => path.Nodes.Select(node => node.Id).ToArray()).ToArray());

        // The product declares two capabilities without records; the walk hit
        // their unresolved has-capability edges and reports them as gaps
        // instead of silently stopping as if the route were complete.
        Assert.Equal(
            [
                "capability:endpoint-telemetry-collection",
                "capability:policy-enforcement",
            ],
            report.UnresolvedReferences
                .Select(gap => gap.Edge.MissingEndpointIds.Single())
                .ToArray());
        Assert.All(report.UnresolvedReferences, gap =>
        {
            Assert.Equal("has-capability", gap.StepName);
            Assert.Equal("product:customer-agent", gap.FromNodeId);
        });

        Golden.AssertMatches("cross-domain/purpose-to-technical-systems.txt", ExplainedPath.Render(report));
    }

    [Fact]
    public void TheReverseTraversalFromACapabilityEnumeratesTheRealizingTechnicalSystems()
    {
        var graph = LoadSharedGraph();

        // Which technical systems realize a capability: every service that
        // implements it.
        var path = Assert.Single(
            CanonicalTraversals.CapabilityToRealizingTechnicalSystems(graph).Execute());

        Assert.Equal(
            [
                "capability:software-update-delivery",
                "service:release-distribution",
            ],
            path.Nodes.Select(node => node.Id).ToArray());

        Golden.AssertMatches(
            "cross-domain/capability-to-realizing-technical-systems.txt",
            ExplainedPath.Render(path));
    }

    [Fact]
    public void TheArtifactTraversalReachesBuildSourceCommitProvenanceAndDistribution()
    {
        var graph = LoadSharedGraph();

        // The declared build chain: the artifact's build pipeline, the source
        // repository the pipeline builds from, and the source commit in it.
        var buildAndSource = Assert.Single(
            CanonicalTraversals.ArtifactBuildAndSource(graph).Execute());
        Assert.Equal(
            [
                "artifact:customer-agent-2.8.4",
                "build-pipeline:customer-agent-release",
                "repository:customer-agent",
                CommitId,
            ],
            buildAndSource.Nodes.Select(node => node.Id).ToArray());

        // The attested derivation chain: the build run the provenance
        // attestation binds the artifact to, that run's source commit, and
        // its repository.
        var attested = Assert.Single(
            CanonicalTraversals.ArtifactAttestedDerivation(graph).Execute());
        Assert.Equal(
            [
                "artifact:customer-agent-2.8.4",
                "build-run:84125",
                CommitId,
                "repository:customer-agent",
            ],
            attested.Nodes.Select(node => node.Id).ToArray());

        // The provenance information that exists for the artifact: the
        // produced-by edge to the build run carries the Sigstore attestation
        // record alongside the CI record that names the run.
        var producedBy = attested.Steps[0];
        Assert.Equal("produced-by", producedBy.Name);
        Assert.Contains(producedBy.Edge.Provenance.Sources, record =>
            record.Kind == SourceKind.ProvenanceAttestation
            && record.Locator == AttestationLocator
            && record.Version == "slsa-provenance/v1");
        Assert.Contains(producedBy.Edge.Provenance.Sources, record =>
            record.Kind == SourceKind.Ci && record.Locator == CiLocator);

        // Where the artifact is distributed.
        var distribution = Assert.Single(
            CanonicalTraversals.ArtifactDistribution(graph).Execute());
        Assert.Equal(
            ["artifact:customer-agent-2.8.4", "aws:cloudfront:customer-downloads"],
            distribution.Nodes.Select(node => node.Id).ToArray());

        Golden.AssertMatches(
            "cross-domain/artifact-to-source-and-distribution.txt",
            ExplainedPath.Render([buildAndSource, attested, distribution]));
    }

    [Fact]
    public void TheRiskToControlTraversalReturnsTheExpectedNodeSequences()
    {
        var graph = LoadSharedGraph();

        var report = CanonicalTraversals.RiskToControlAndObservations(graph).ExecuteWithGaps();

        // Why the risk matters and how it is controlled: business impact,
        // control adoption, implementation, and assertion — one path per
        // observation the assertion produced, in observation order.
        Assert.Equal(ObservationIds, report.Paths.Select(path => path.End.Id).ToArray());
        Assert.All(report.Paths, path => Assert.Equal(
            [
                "risk:artifact-tampering",
                "impact:artifact-compromise",
                "control-adoption:ca-artifact-supply-chain",
                "implementation:signed-release-pipeline",
                "assertion:prod-artifact-provenance",
                path.End.Id,
            ],
            path.Nodes.Select(node => node.Id).ToArray()));

        // No unresolved reference on this chain.
        Assert.Empty(report.UnresolvedReferences);

        Golden.AssertMatches("cross-domain/risk-to-control-and-observations.txt", ExplainedPath.Render(report));
    }

    [Fact]
    public void TheExplainedPathRenderingNamesTheTypedEdgeBetweenEveryPairOfAdjacentNodes()
    {
        var graph = LoadSharedGraph();

        var path = Assert.Single(CanonicalTraversals.ResourceToPurpose(graph).Execute());

        // Every hop renders the queried name, the reached node, the traversed
        // edge type, and the direction — the typed edge between each pair of
        // adjacent nodes, so the path reads as a justification.
        Assert.Equal(
            """
            aws:s3:prod-release-artifacts
              → used-by → service:release-distribution [depends-on, inverse]
              → implements → capability:software-update-delivery [implemented-by, inverse]
              → belongs-to → product:customer-agent [has-capability, inverse]
              → fulfills → business-purpose:secure-endpoint-management [fulfills, forward]
            """,
            ExplainedPath.Render(path));
    }

    [Fact]
    public void AReportRendersUnresolvedReferencesAlongsideItsCompletedPaths()
    {
        var graph = LoadSharedGraph();

        var report = CanonicalTraversals.PurposeToTechnicalSystems(graph).ExecuteWithGaps();

        Assert.Contains(
            """
            Unresolved references:
              has-capability → capability:endpoint-telemetry-collection (from product:customer-agent)
              has-capability → capability:policy-enforcement (from product:customer-agent)
            """,
            ExplainedPath.Render(report),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ATraversalThatHitsAnUnresolvedReferenceReportsTheGapRatherThanStoppingSilently()
    {
        var graph = new TypedPropertyGraph(EdgeTypeRegistry.Slice);
        graph.AddNode("product:no-records", Node("Product"));
        graph.AddEdge(
            "has-capability",
            "product:no-records",
            Node("Product"),
            "capability:never-recorded",
            Node("ProductCapability"));

        var query = new GraphTraversal(graph, "product:no-records").Step("has-capability");

        // The plain execution returns no path: no route could step onto a
        // missing node. Without the report that reads as "matched nothing".
        Assert.Empty(query.Execute());

        // The gap report says what actually happened: the walk hit an
        // unresolved reference, so the traversal is incomplete rather than
        // complete with no result.
        var report = query.ExecuteWithGaps();
        Assert.Empty(report.Paths);
        var gap = Assert.Single(report.UnresolvedReferences);
        Assert.Equal("has-capability", gap.StepName);
        Assert.Equal("product:no-records", gap.FromNodeId);
        Assert.Equal("capability:never-recorded", gap.Edge.MissingEndpointIds.Single());

        Assert.Equal(
            """
            Unresolved references:
              has-capability → capability:never-recorded (from product:no-records)
            """,
            ExplainedPath.Render(report));
    }

    private static TypedPropertyGraph LoadSharedGraph()
    {
        // The shared graph both worlds feed: the declared records under
        // data/** and the observed facts the fixture adapters normalize from
        // fixtures/**.
        var graph = new TypedPropertyGraph(EdgeTypeRegistry.Slice);
        var world = RepositoryDataLoader.Load(TestWorld.RepositoryRoot);
        DeclaredRecordsLoader.ApplyAll(graph, world.Records);
        FixtureAdapterRegistry.MockWorld.ApplyAll(graph, world.Fixtures);
        return graph;
    }

    private static NodeType Node(string name) =>
        EdgeTypeRegistry.Slice.NodeTypes.Single(type => type.Name == name);
}
