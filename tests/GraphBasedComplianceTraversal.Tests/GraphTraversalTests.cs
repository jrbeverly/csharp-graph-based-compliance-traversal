using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ingestion;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Traversal;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Pins the traversal engine: typed, directional path queries over the graph,
/// deterministic ordering, cycle-safe execution that reports the repeated
/// node, and results carrying the full ordered trail. The scenarios run
/// against the repository's own declared world and small synthetic graphs.
/// </summary>
public sealed class GraphTraversalTests
{
    private static readonly string[] ObservationIds =
    [
        "observation:obs-2026-08-08-001",
        "observation:obs-2026-08-07-001",
        "observation:obs-2026-08-06-001",
        "observation:obs-2026-08-05-001",
        "observation:obs-2026-07-15-001",
    ];

    [Fact]
    public void TheInverseDependsOnTraversalReachesTheServiceAndOnward()
    {
        var graph = LoadDeclaredGraph();

        // From the bucket, 'used-by' — the inverse name of depends-on —
        // reaches the service that depends on it.
        var fromBucket = Assert.Single(
            new GraphTraversal(graph, "aws:s3:prod-release-artifacts").Step("used-by").Execute());
        Assert.Equal("service:release-distribution", fromBucket.End.Id);
        var usedBy = Assert.Single(fromBucket.Steps);
        Assert.Equal("used-by", usedBy.Name);
        Assert.Equal(EdgeDirection.Inverse, usedBy.Direction);
        Assert.Equal("depends-on", usedBy.Edge.EdgeType.ForwardName);

        // The explicit inverse-direction form of the same step is equivalent.
        var inverse = Assert.Single(
            new GraphTraversal(graph, "aws:s3:prod-release-artifacts")
                .Step("depends-on", EdgeDirection.Inverse)
                .Execute());
        Assert.Equal("service:release-distribution", inverse.End.Id);
        Assert.Equal(EdgeDirection.Inverse, Assert.Single(inverse.Steps).Direction);

        // And onward: the business chain the bucket serves.
        var onward = Assert.Single(
            new GraphTraversal(graph, "aws:s3:prod-release-artifacts")
                .Step("used-by")
                .Step("implements")
                .Step("belongs-to")
                .Step("fulfills")
                .Execute());
        Assert.Equal(
            [
                "aws:s3:prod-release-artifacts",
                "service:release-distribution",
                "capability:software-update-delivery",
                "product:customer-agent",
                "business-purpose:secure-endpoint-management",
            ],
            onward.Nodes.Select(node => node.Id).ToArray());
    }

    [Fact]
    public void TraversalMovesBothDirectionsAcrossTheSameEdgesWithoutDuplicatedDefinitions()
    {
        var graph = LoadDeclaredGraph();

        // Business -> infra follows the forward names.
        var down = new GraphTraversal(graph, "business-purpose:secure-endpoint-management")
            .Step("fulfilled-by")
            .Step("has-capability")
            .Step("implemented-by")
            .Step("depends-on")
            .Execute();
        Assert.Equal(
            ["aws:cloudfront:customer-downloads", "aws:s3:prod-release-artifacts"],
            down.Select(path => path.End.Id).ToArray());

        // Infra -> business follows the inverse names over the same stored
        // edges.
        var up = Assert.Single(
            new GraphTraversal(graph, "aws:s3:prod-release-artifacts")
                .Step("used-by")
                .Step("implements")
                .Step("belongs-to")
                .Step("fulfills")
                .Execute());
        Assert.Equal("business-purpose:secure-endpoint-management", up.End.Id);

        // The two queries traverse the same edge instances: the bucket's
        // used-by step is the same stored edge as the service's depends-on
        // step toward the bucket.
        var dependencyOnBucket = down[1].Steps[^1];
        Assert.Same(dependencyOnBucket.Edge, up.Steps[0].Edge);
        Assert.Equal(EdgeDirection.Forward, dependencyOnBucket.Direction);
        Assert.Equal(EdgeDirection.Inverse, up.Steps[0].Direction);

        // And no traversal adds or duplicates any edge definition.
        Assert.Equal(42, graph.Edges.Count);
    }

    [Fact]
    public void AnEdgeStoredUnderItsInverseNameTraversesBothDirections()
    {
        var graph = EmptyGraph();
        var capability = graph.AddNode("capability:software-update-delivery", Node("ProductCapability"));
        var service = graph.AddNode("service:release-distribution", Node("Service"));
        graph.AddEdge("implements", service, capability); // stored under the inverse name

        var forward = Assert.Single(
            new GraphTraversal(graph, capability).Step("implemented-by").Execute());
        Assert.Equal(service.Id, forward.End.Id);
        Assert.Equal(EdgeDirection.Forward, Assert.Single(forward.Steps).Direction);

        var inverse = Assert.Single(
            new GraphTraversal(graph, service).Step("implements").Execute());
        Assert.Equal(capability.Id, inverse.End.Id);
        Assert.Equal(EdgeDirection.Inverse, Assert.Single(inverse.Steps).Direction);

        // The direction-constrained forms agree: inverse from the service
        // reaches the capability, forward from the service matches nothing.
        var constrained = Assert.Single(
            new GraphTraversal(graph, service).Step("implemented-by", EdgeDirection.Inverse).Execute());
        Assert.Equal(capability.Id, constrained.End.Id);
        Assert.Empty(new GraphTraversal(graph, service).Step("implemented-by", EdgeDirection.Forward).Execute());
    }

    [Fact]
    public void ATraversalOverACycleTerminatesAndReportsTheRepeatedNode()
    {
        var graph = LoadDeclaredGraph();

        // service -> distribution -> bucket -> service is a cycle of the
        // declared world: depends-on, then origin, then used-by closes the
        // loop.
        var cyclic = Assert.Single(
            new GraphTraversal(graph, "service:release-distribution")
                .Step("depends-on")
                .Step("origin")
                .Step("used-by")
                .Execute());

        Assert.True(cyclic.IsCycle);
        Assert.Same(cyclic.Start, cyclic.End);
        Assert.Equal("service:release-distribution", cyclic.End.Id);
        Assert.Equal(
            [
                "service:release-distribution",
                "aws:cloudfront:customer-downloads",
                "aws:s3:prod-release-artifacts",
                "service:release-distribution",
            ],
            cyclic.Nodes.Select(node => node.Id).ToArray());

        // A query that would loop past the repeated node terminates with no
        // invented paths: the closed loop cannot complete a further step.
        Assert.Empty(
            new GraphTraversal(graph, "service:release-distribution")
                .Step("depends-on")
                .Step("origin")
                .Step("used-by")
                .Step("implements")
                .Execute());
    }

    [Fact]
    public void EveryResultExposesItsOrderedTrailOfTypedSteps()
    {
        var graph = LoadDeclaredGraph();

        var path = Assert.Single(
            new GraphTraversal(graph, "aws:s3:prod-release-artifacts")
                .Step("used-by")
                .Step("implements")
                .Step("belongs-to")
                .Step("fulfills")
                .Execute());

        Assert.False(path.IsCycle);
        Assert.Same(graph.GetNode("aws:s3:prod-release-artifacts"), path.Start);
        Assert.Equal(
            ["used-by", "implements", "belongs-to", "fulfills"],
            path.Steps.Select(step => step.Name).ToArray());
        Assert.Equal(
            [EdgeDirection.Inverse, EdgeDirection.Inverse, EdgeDirection.Inverse, EdgeDirection.Forward],
            path.Steps.Select(step => step.Direction).ToArray());
        Assert.Equal(
            ["depends-on", "implemented-by", "has-capability", "fulfills"],
            path.Steps.Select(step => step.Edge.EdgeType.ForwardName).ToArray());
        Assert.Equal(
            [
                "service:release-distribution",
                "capability:software-update-delivery",
                "product:customer-agent",
                "business-purpose:secure-endpoint-management",
            ],
            path.Steps.Select(step => step.Node.Id).ToArray());
        Assert.Equal(path.Steps.Select(step => step.Node).ToArray(), path.Nodes.Skip(1).ToArray());
    }

    [Fact]
    public void ResultsAreDeterministicAndFanOutInInsertionOrder()
    {
        var graph = LoadDeclaredGraph();
        var produces = new GraphTraversal(graph, "assertion:prod-artifact-provenance").Step("produces");

        var first = produces.Execute();
        var second = produces.Execute();
        Assert.Equal(first.Select(path => path.End.Id), second.Select(path => path.End.Id));
        Assert.Equal(ObservationIds, first.Select(path => path.End.Id).ToArray());

        // Fan-out follows the graph's insertion order: the service's two
        // dependencies come back in record order.
        Assert.Equal(
            ["aws:cloudfront:customer-downloads", "aws:s3:prod-release-artifacts"],
            new GraphTraversal(graph, "service:release-distribution").Step("depends-on").Execute()
                .Select(path => path.End.Id)
                .ToArray());
    }

    [Fact]
    public void UnresolvedEdgesAreNotFollowedAndDoNotInventNodes()
    {
        var graph = LoadDeclaredGraph();

        // The product's two capabilities without records stay unresolved
        // references; the traversal returns only the resolved route.
        var path = Assert.Single(
            new GraphTraversal(graph, "product:customer-agent").Step("has-capability").Execute());
        Assert.Equal("capability:software-update-delivery", path.End.Id);
        Assert.True(Assert.Single(path.Steps).Edge.IsResolved);
    }

    [Fact]
    public void AQueryWithNoStepsReportsTheStartNode()
    {
        var graph = LoadDeclaredGraph();

        var path = Assert.Single(new GraphTraversal(graph, "aws:s3:prod-release-artifacts").Execute());
        Assert.Empty(path.Steps);
        Assert.False(path.IsCycle);
        Assert.Same(path.Start, path.End);
    }

    [Fact]
    public void ADeclaredNameThatMatchesNothingIsAnEmptyResult()
    {
        var graph = LoadDeclaredGraph();

        // "used-by" is declared, but nothing reads it from the business purpose.
        Assert.Empty(
            new GraphTraversal(graph, "business-purpose:secure-endpoint-management").Step("used-by").Execute());
    }

    [Fact]
    public void AnUndeclaredStepNameIsRejected()
    {
        var graph = LoadDeclaredGraph();

        var exception = Assert.Throws<ArgumentException>(
            () => new GraphTraversal(graph, "aws:s3:prod-release-artifacts").Step("no-such-edge"));

        Assert.Contains("not declared", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStartNodeThatIsNotInTheGraphIsRejected()
    {
        var graph = LoadDeclaredGraph();

        Assert.Throws<KeyNotFoundException>(
            () => new GraphTraversal(graph, "aws:s3:no-such-bucket"));
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
}
