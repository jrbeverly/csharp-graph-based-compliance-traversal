using GraphBasedComplianceTraversal.Engine.Adapters;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ingestion;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Observations;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.State;
using GraphBasedComplianceTraversal.Engine.Views;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Pins the named domain views — architecture, security, and compliance as
/// projection functions over one shared graph, each covered by a golden
/// rendering. The compliance view in particular is pinned as a derived view:
/// produced entirely by the supply-chain assurance grammar traversed from
/// the requirement, referencing the shared nodes rather than restating
/// control descriptions, and carrying the observation store's current state —
/// the seeded FAIL and STALE signals included — instead of a blanket
/// success. The scenarios run against the shared world: the repository's
/// declared <c>data/**</c> records merged with the observed
/// <c>fixtures/**</c> facts, plus the observation store fed from the same
/// records.
/// </summary>
public sealed class ViewProjectionTests
{
    private static readonly string[] ObservationIds =
    [
        "observation:obs-2026-08-08-001",
        "observation:obs-2026-08-07-001",
        "observation:obs-2026-08-06-001",
        "observation:obs-2026-08-05-001",
        "observation:obs-2026-07-15-001",
    ];

    private static readonly string[] SharedControlChainIds =
    [
        "control-adoption:ca-artifact-supply-chain",
        "implementation:signed-release-pipeline",
        "assertion:prod-artifact-provenance",
    ];

    [Fact]
    public void TheArchitectureViewProjectsTheReleaseChainOverSharedNodes()
    {
        var graph = LoadSharedGraph();

        var view = DomainViews.Architecture(graph);

        Assert.Equal("architecture", view.Name);
        Assert.Equal("service:release-distribution", view.Subject.Id);

        // The release architecture in one typed chain: the service, the
        // distribution it depends on, the artifact distributed through it,
        // the build pipeline that produced the artifact, and the repository
        // that build draws from — every component the planning input names.
        var path = Assert.Single(view.Paths);
        Assert.False(path.IsCycle);
        Assert.Equal(
            [
                "service:release-distribution",
                "aws:cloudfront:customer-downloads",
                "artifact:customer-agent-2.8.4",
                "build-pipeline:customer-agent-release",
                "repository:customer-agent",
            ],
            path.Nodes.Select(node => node.Id).ToArray());
        Assert.Equal(
            ["depends-on", "distributes", "produced-by", "source"],
            path.Steps.Select(step => step.Name).ToArray());

        // The view projects the distinct shared nodes in first-seen order,
        // and carries no observation state — its paths reach none.
        Assert.Equal(
            path.Nodes.Select(node => node.Id),
            view.Nodes.Select(node => node.Id));
        Assert.Empty(view.Observations);

        Golden.AssertMatches("views/architecture.txt", ViewRendering.Render(view));
    }

    [Fact]
    public void TheSecurityViewProjectsTheRiskToControlChain()
    {
        var graph = LoadSharedGraph();

        var view = DomainViews.Security(graph);

        Assert.Equal("security", view.Name);
        Assert.Equal("risk:artifact-tampering", view.Subject.Id);

        // Why the risk matters and how it is controlled: impact, adoption,
        // implementation, and assertion — one path per observation the
        // assertion produced, in observation order.
        Assert.Equal(ObservationIds, view.Paths.Select(path => path.End.Id).ToArray());
        Assert.All(view.Paths, path => Assert.Equal(
            [
                "risk:artifact-tampering",
                "impact:artifact-compromise",
                "control-adoption:ca-artifact-supply-chain",
                "implementation:signed-release-pipeline",
                "assertion:prod-artifact-provenance",
                path.End.Id,
            ],
            path.Nodes.Select(node => node.Id).ToArray()));
        Assert.Empty(view.Observations);

        Golden.AssertMatches("views/security.txt", ViewRendering.Render(view));
    }

    [Fact]
    public void TheComplianceViewDerivesTheRequirementByTraversalAndCarriesTheObservationState()
    {
        var (graph, store) = LoadSharedWorld();

        var view = DomainViews.Compliance(graph, store);

        Assert.Equal("compliance", view.Name);
        Assert.Equal("requirement:nis2-article-21-supply-chain", view.Subject.Id);

        // Produced entirely by traversal: the supply-chain assurance grammar
        // evaluated from the requirement — the requirement functions as the
        // entry point into the organizational graph, and nothing about the
        // controls is restated, only referenced.
        Assert.Equal(ObservationIds, view.Paths.Select(path => path.End.Id).ToArray());
        Assert.All(view.Paths, path => Assert.Equal(
            [
                "requirement:nis2-article-21-supply-chain",
                "control-adoption:ca-artifact-supply-chain",
                "implementation:signed-release-pipeline",
                "assertion:prod-artifact-provenance",
                path.End.Id,
            ],
            path.Nodes.Select(node => node.Id).ToArray()));

        // The current observation state the store surfaces, in path order:
        // three passes, the recorded FAIL of the blocked unsigned upload,
        // and the two outdated-validator entries surfaced STALE — never a
        // blanket success.
        Assert.Equal(ObservationIds, view.Observations.Select(observation => observation.ObservationId).ToArray());
        Assert.Equal(
            [
                ValidationState.Pass,
                ValidationState.Pass,
                ValidationState.Fail,
                ValidationState.Stale,
                ValidationState.Stale,
            ],
            view.Observations.Select(observation => observation.State).ToArray());
        Assert.Equal(
            [
                ValidationState.Pass,
                ValidationState.Pass,
                ValidationState.Fail,
                ValidationState.Pass,
                ValidationState.Pass,
            ],
            view.Observations.Select(observation => observation.RecordedState).ToArray());

        var failed = view.Observations[2];
        Assert.False(failed.IsStale);

        var staleByOldest = view.Observations[3];
        Assert.True(staleByOldest.IsStale);
        Assert.Equal("provenance-prober@3.1.6", staleByOldest.ValidatorVersion);

        var staleByOldValidator = view.Observations[4];
        Assert.True(staleByOldValidator.IsStale);
        Assert.Equal("provenance-prober@3.1.5", staleByOldValidator.ValidatorVersion);

        Golden.AssertMatches("views/compliance.txt", ViewRendering.Render(view));
    }

    [Fact]
    public void TheViewsReferenceTheSameUnderlyingNodes()
    {
        var (graph, store) = LoadSharedWorld();

        var architecture = DomainViews.Architecture(graph);
        var security = DomainViews.Security(graph);
        var compliance = DomainViews.Compliance(graph, store);

        // Every node every view projects is the graph's own instance — a
        // projection references the shared nodes, never copies them.
        foreach (var view in new[] { architecture, security, compliance })
        {
            foreach (var node in view.Nodes)
            {
                Assert.Same(graph.GetNode(node.Id), node);
            }
        }

        // Where the views overlap, they reference the same instances: the
        // security and compliance views share the control adoption, the
        // implementation, the assertion, and all five observations.
        foreach (var sharedId in SharedControlChainIds.Concat(ObservationIds))
        {
            Assert.Same(
                security.Nodes.Single(node => node.Id == sharedId),
                compliance.Nodes.Single(node => node.Id == sharedId));
        }

        // The architecture view's artifact is the very node the compliance
        // and security views' observations measured, stored once in the
        // shared graph.
        Assert.Same(
            architecture.Nodes.Single(node => node.Id == "artifact:customer-agent-2.8.4"),
            graph.GetNode("artifact:customer-agent-2.8.4"));
    }

    [Fact]
    public void TheComplianceViewCarriesNoControlDescriptionOfItsOwn()
    {
        var (graph, store) = LoadSharedWorld();

        var view = DomainViews.Compliance(graph, store);

        // The view's adoption and implementation nodes are the shared nodes
        // themselves — the descriptions they carry are the graph's records,
        // not view-owned copies, and the view stores no property text of
        // its own.
        var adoption = view.Nodes.Single(node => node.Id == "control-adoption:ca-artifact-supply-chain");
        var implementation = view.Nodes.Single(node => node.Id == "implementation:signed-release-pipeline");
        Assert.Same(graph.GetNode(adoption.Id), adoption);
        Assert.Same(graph.GetNode(implementation.Id), implementation);
    }

    [Fact]
    public void ARequirementWithoutAnAssuranceChainProjectsNoPathsAndNoObservationState()
    {
        // A world whose requirement exists but is satisfied by nothing: the
        // grammar's verdict is an explicit no-valid-path, and the view
        // carries no fabricated paths and no observation state.
        var graph = new TypedPropertyGraph(EdgeTypeRegistry.Slice);
        graph.AddNode(DomainViews.ComplianceSubjectId, Node("RegulatoryRequirement"));
        var store = new ObservationStore();

        var view = DomainViews.Compliance(graph, store);

        Assert.Empty(view.Paths);
        Assert.Empty(view.Nodes);
        Assert.Empty(view.Observations);
        Assert.Equal(DomainViews.ComplianceSubjectId, view.Subject.Id);
    }

    [Fact]
    public void RenderingTheSameViewTwiceProducesTheSameText()
    {
        var (graph, store) = LoadSharedWorld();

        var first = ViewRendering.Render(DomainViews.Compliance(graph, store));
        var second = ViewRendering.Render(DomainViews.Compliance(graph, store));

        Assert.Equal(first, second);
    }

    private static TypedPropertyGraph LoadSharedGraph() => LoadSharedWorld().Graph;

    private static (TypedPropertyGraph Graph, ObservationStore Store) LoadSharedWorld()
    {
        // The shared world all views project over: the declared records
        // under data/** merged with the observed facts the fixture adapters
        // normalize from fixtures/**, and the observation store fed the
        // recorded history from the same records.
        var graph = new TypedPropertyGraph(EdgeTypeRegistry.Slice);
        var world = RepositoryDataLoader.Load(TestWorld.RepositoryRoot);
        DeclaredRecordsLoader.ApplyAll(graph, world.Records);
        FixtureAdapterRegistry.MockWorld.ApplyAll(graph, world.Fixtures);

        var store = new ObservationStore();
        HistoricalObservationLoader.ApplyAll(store, world.Records);
        return (graph, store);
    }

    private static NodeType Node(string name) =>
        EdgeTypeRegistry.Slice.NodeTypes.Single(type => type.Name == name);
}
