using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ingestion;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Traversal;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Pins the path grammars: named declarations of the typed edge sequences that
/// constitute a semantically valid path, evaluation that returns every valid
/// path while surfacing same-terminal paths built from wrong edges as
/// rejected, and the explicit no-valid-path verdict when nothing matches. The
/// scenarios run against the repository's own declared world and small
/// synthetic graphs.
/// </summary>
public sealed class PathGrammarTests
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
    public void TheAssurancePathGrammarAcceptsTheIntendedPathAndRejectsTheSemanticallyWrongOne()
    {
        var graph = DiamondGraph();
        var grammar = AssurancePathGrammar(graph.Registry);

        // The grammar declares the typed supply-chain assurance path:
        // requirement → (satisfied-by) control-adoption → (implemented-by)
        // implementation → (validated-by) assertion → (produces) observation.
        Assert.Equal("supply-chain-assurance", grammar.Name);
        Assert.Equal("RegulatoryRequirement", grammar.StartType?.Name);
        Assert.Equal("Observation", grammar.EndType?.Name);
        Assert.Equal(
            ["satisfied-by", "implemented-by", "validated-by", "produces"],
            grammar.Steps.Select(step => step.Name).ToArray());

        var result = grammar.Evaluate(graph, "requirement:req-a");

        // The intended path is accepted, with its full typed trail.
        var valid = Assert.Single(result.ValidPaths);
        Assert.Equal(PathGrammarVerdict.ValidPath, result.Verdict);
        Assert.True(result.HasValidPath);
        Assert.Equal(
            [
                "requirement:req-a",
                "control-adoption:ca-a",
                "implementation:impl-a",
                "assertion:assert-a",
                "observation:obs-a",
            ],
            valid.Nodes.Select(node => node.Id).ToArray());
        Assert.Equal(
            [EdgeDirection.Inverse, EdgeDirection.Forward, EdgeDirection.Inverse, EdgeDirection.Forward],
            valid.Steps.Select(step => step.Direction).ToArray());
        Assert.True(grammar.Matches(valid));

        // The same-endpoint path through the semantically wrong edges — the
        // adoption validated directly by the assertion, skipping the
        // implementation hop — is rejected, not accepted.
        var rejected = Assert.Single(result.RejectedPaths);
        Assert.Equal(
            [
                "requirement:req-a",
                "control-adoption:ca-a",
                "assertion:assert-a",
                "observation:obs-a",
            ],
            rejected.Nodes.Select(node => node.Id).ToArray());
        Assert.Equal(
            ["satisfied-by", "validated-by", "produces"],
            rejected.Steps.Select(step => step.Name).ToArray());
        Assert.False(grammar.Matches(rejected));
        Assert.DoesNotContain(rejected, result.ValidPaths);
    }

    [Fact]
    public void TheAssurancePathGrammarEvaluatesTheDeclaredWorld()
    {
        var graph = LoadDeclaredGraph();
        var grammar = AssurancePathGrammar(graph.Registry);

        var result = grammar.Evaluate(graph, "requirement:nis2-article-21-supply-chain");
        Assert.Equal(PathGrammarVerdict.ValidPath, result.Verdict);

        // Every valid path follows the intended supply-chain assurance chain
        // end to end — one per observation the assertion produced.
        Assert.Equal(ObservationIds, result.ValidPaths.Select(path => path.End.Id).ToArray());
        foreach (var path in result.ValidPaths)
        {
            Assert.Equal(
                [
                    "requirement:nis2-article-21-supply-chain",
                    "control-adoption:ca-artifact-supply-chain",
                    "implementation:signed-release-pipeline",
                    "assertion:prod-artifact-provenance",
                ],
                path.Nodes.Take(4).Select(node => node.Id).ToArray());
            Assert.Equal(
                ["satisfied-by", "implemented-by", "validated-by", "produces"],
                path.Steps.Select(step => step.Name).ToArray());
            Assert.True(grammar.Matches(path));
            Assert.False(path.IsCycle);
        }

        // Rejected: the semantically wrong routes to the same terminals —
        // the adoption validated by the assertion without an implementation
        // hop (one per observation) and the scope/observed-by shortcuts
        // around the assertion entirely. All of them are surfaced, and none
        // is ever reported as valid.
        Assert.Equal(9, result.RejectedPaths.Count);
        foreach (var path in result.RejectedPaths)
        {
            Assert.False(grammar.Matches(path));
            Assert.DoesNotContain(path, result.ValidPaths);
        }

        Assert.Equal(
            ObservationIds,
            result.RejectedPaths
                .Where(path => AssertIs(path, "satisfied-by", "validated-by", "produces"))
                .Select(path => path.End.Id)
                .ToArray());
        Assert.Contains(result.RejectedPaths, path => PathIs(path,
            "requirement:nis2-article-21-supply-chain",
            "control-adoption:ca-artifact-supply-chain",
            "assertion:prod-artifact-provenance",
            "observation:obs-2026-08-08-001"));
        Assert.Contains(result.RejectedPaths, path => PathIs(path,
            "requirement:nis2-article-21-supply-chain",
            "control-adoption:ca-artifact-supply-chain",
            "artifact:customer-agent-2.8.4",
            "observation:obs-2026-08-08-001"));
        Assert.Contains(result.RejectedPaths, path => PathIs(path,
            "requirement:nis2-article-21-supply-chain",
            "control-adoption:ca-artifact-supply-chain",
            "aws:s3:prod-release-artifacts",
            "artifact:customer-agent-2.8.4",
            "observation:obs-2026-08-08-001"));
        Assert.Contains(result.RejectedPaths, path => PathIs(path,
            "requirement:nis2-article-21-supply-chain",
            "control-adoption:ca-artifact-supply-chain",
            "build-pipeline:customer-agent-release",
            "artifact:customer-agent-2.8.4",
            "observation:obs-2026-08-08-001"));
        Assert.Contains(result.RejectedPaths, path => PathIs(path,
            "requirement:nis2-article-21-supply-chain",
            "control-adoption:ca-artifact-supply-chain",
            "aws:cloudfront:customer-downloads",
            "artifact:customer-agent-2.8.4",
            "observation:obs-2026-08-08-001"));
    }

    [Fact]
    public void NoMatchingPathIsAnExplicitNoValidPathVerdict()
    {
        // A world whose assurance chain breaks before the observation hop:
        // the requirement is adopted, implemented, and asserted, but no
        // observation was ever produced.
        var graph = EmptyGraph();
        var requirement = graph.AddNode("requirement:req-incomplete", Node("RegulatoryRequirement"));
        var adoption = graph.AddNode("control-adoption:ca-incomplete", Node("ControlAdoption"));
        var implementation = graph.AddNode("implementation:impl-incomplete", Node("ControlImplementation"));
        var assertion = graph.AddNode("assertion:assert-incomplete", Node("Assertion"));
        graph.AddEdge("satisfies", adoption, requirement);
        graph.AddEdge("implemented-by", adoption, implementation);
        graph.AddEdge("validates", assertion, implementation);

        var result = AssurancePathGrammar(graph.Registry).Evaluate(graph, requirement);

        // No fabricated path: the verdict states no-valid-path explicitly,
        // and neither the valid nor the rejected side invents a route.
        Assert.Equal(PathGrammarVerdict.NoValidPath, result.Verdict);
        Assert.False(result.HasValidPath);
        Assert.Empty(result.ValidPaths);
        Assert.Empty(result.RejectedPaths);

        // A start node whose type is not the grammar's start type is simply
        // outside the grammar's governance: no valid path, no exception.
        var fromAdoption = AssurancePathGrammar(graph.Registry).Evaluate(graph, adoption);
        Assert.Equal(PathGrammarVerdict.NoValidPath, fromAdoption.Verdict);
        Assert.Empty(fromAdoption.ValidPaths);
        Assert.Empty(fromAdoption.RejectedPaths);
    }

    [Fact]
    public void MultipleValidPathsForOneRequirementAreAllReturned()
    {
        var graph = EmptyGraph();
        var requirement = graph.AddNode("requirement:req-multi", Node("RegulatoryRequirement"));
        var firstAdoption = graph.AddNode("control-adoption:ca-first", Node("ControlAdoption"));
        var secondAdoption = graph.AddNode("control-adoption:ca-second", Node("ControlAdoption"));
        var firstImplementation = graph.AddNode("implementation:impl-first", Node("ControlImplementation"));
        var secondImplementation = graph.AddNode("implementation:impl-second", Node("ControlImplementation"));
        var firstAssertion = graph.AddNode("assertion:assert-first", Node("Assertion"));
        var secondAssertion = graph.AddNode("assertion:assert-second", Node("Assertion"));
        var firstObservation = graph.AddNode("observation:obs-first", Node("Observation"));
        var secondObservation = graph.AddNode("observation:obs-second", Node("Observation"));

        // Two complete alternative assurance chains for the same requirement.
        graph.AddEdge("satisfies", firstAdoption, requirement);
        graph.AddEdge("satisfies", secondAdoption, requirement);
        graph.AddEdge("implemented-by", firstAdoption, firstImplementation);
        graph.AddEdge("implemented-by", secondAdoption, secondImplementation);
        graph.AddEdge("validates", firstAssertion, firstImplementation);
        graph.AddEdge("validates", secondAssertion, secondImplementation);
        graph.AddEdge("produces", firstAssertion, firstObservation);
        graph.AddEdge("produces", secondAssertion, secondObservation);

        // And a semantically wrong route alongside the second chain: the
        // second adoption validated directly, without its implementation.
        graph.AddEdge("validates", secondAssertion, secondAdoption);

        var grammar = AssurancePathGrammar(graph.Registry);
        var result = grammar.Evaluate(graph, requirement);

        // Both valid paths are returned, not just the first one found.
        Assert.Equal(PathGrammarVerdict.ValidPath, result.Verdict);
        Assert.Equal(2, result.ValidPaths.Count);
        Assert.Equal(
            ["observation:obs-first", "observation:obs-second"],
            result.ValidPaths.Select(path => path.End.Id).ToArray());
        Assert.Equal(
            ["control-adoption:ca-first", "control-adoption:ca-second"],
            result.ValidPaths.Select(path => path.Nodes[1].Id).ToArray());

        // The wrong route is rejected, never counted as satisfaction.
        var rejected = Assert.Single(result.RejectedPaths);
        Assert.False(grammar.Matches(rejected));
        Assert.Equal(
            [
                "requirement:req-multi",
                "control-adoption:ca-second",
                "assertion:assert-second",
                "observation:obs-second",
            ],
            rejected.Nodes.Select(node => node.Id).ToArray());
    }

    [Fact]
    public void StepsMayBeDeclaredByEdgeTypeAndDirection()
    {
        var graph = DiamondGraph();

        var byEdgeType = new PathGrammar(graph.Registry, "supply-chain-assurance")
            .Step(Edge("satisfies", "satisfied-by", "ControlAdoption", "RegulatoryRequirement"), EdgeDirection.Inverse)
            .Step(Edge("implemented-by", "implements", "ControlAdoption", "ControlImplementation"), EdgeDirection.Forward)
            .Step(Edge("validates", "validated-by", "Assertion", "ControlImplementation"), EdgeDirection.Inverse)
            .Step(Edge("produces", "produced-by", "Assertion", "Observation"), EdgeDirection.Forward);

        // The edge-type declaration is the same grammar as the name-based one.
        Assert.Equal(AssurancePathGrammar(graph.Registry).Steps, byEdgeType.Steps);

        var result = byEdgeType.Evaluate(graph, "requirement:req-a");
        var valid = Assert.Single(result.ValidPaths);
        Assert.Equal(
            [
                "requirement:req-a",
                "control-adoption:ca-a",
                "implementation:impl-a",
                "assertion:assert-a",
                "observation:obs-a",
            ],
            valid.Nodes.Select(node => node.Id).ToArray());
        var rejected = Assert.Single(result.RejectedPaths);
        Assert.Equal("assertion:assert-a", rejected.Nodes[2].Id);
    }

    [Fact]
    public void UndeclaredOrDisconnectedStepsAreRejectedWhenTheGrammarIsBuilt()
    {
        var registry = EdgeTypeRegistry.Slice;

        // An edge type the ontology slice does not declare cannot be a step.
        var undeclared = Assert.Throws<ArgumentException>(
            () => new PathGrammar(registry, "g").Step(
                new EdgeType("some-edge", "some-other", Node("Product"), Node("Service"), "Not declared."),
                EdgeDirection.Forward));
        Assert.Contains("not declared", undeclared.Message, StringComparison.Ordinal);

        // A name the slice does not resolve across the given endpoint types
        // is rejected with the registry's violation.
        Assert.Throws<EdgeTypeViolationException>(
            () => new PathGrammar(registry, "g").Step(
                "satisfied-by", Node("RegulatoryRequirement"), Node("ControlImplementation")));

        // A step that does not chain onto the preceding step's exit node
        // type cannot form a contiguous grammar.
        var disconnected = Assert.Throws<ArgumentException>(
            () => new PathGrammar(registry, "g")
                .Step("satisfied-by", Node("RegulatoryRequirement"), Node("ControlAdoption"))
                .Step("implements", Node("Service"), Node("ProductCapability")));
        Assert.Contains("chain", disconnected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluationWithoutStepsOrFromAMissingStartNodeIsRejected()
    {
        var graph = LoadDeclaredGraph();
        var empty = new PathGrammar(graph.Registry, "no-steps");

        Assert.Throws<InvalidOperationException>(() => empty.Evaluate(graph, "requirement:nis2-article-21-supply-chain"));
        Assert.Throws<KeyNotFoundException>(
            () => AssurancePathGrammar(graph.Registry).Evaluate(graph, "requirement:no-such-requirement"));
    }

    private static PathGrammar AssurancePathGrammar(EdgeTypeRegistry registry) =>
        new PathGrammar(registry, "supply-chain-assurance")
            .Step("satisfied-by", Node("RegulatoryRequirement"), Node("ControlAdoption"))
            .Step("implemented-by", Node("ControlAdoption"), Node("ControlImplementation"))
            .Step("validated-by", Node("ControlImplementation"), Node("Assertion"))
            .Step("produces", Node("Assertion"), Node("Observation"));

    /// <summary>
    /// A requirement whose assurance is declared two ways: the intended chain
    /// through an implementation, and a same-endpoint shortcut in which the
    /// assertion validates the adoption directly.
    /// </summary>
    private static TypedPropertyGraph DiamondGraph()
    {
        var graph = EmptyGraph();
        var requirement = graph.AddNode("requirement:req-a", Node("RegulatoryRequirement"));
        var adoption = graph.AddNode("control-adoption:ca-a", Node("ControlAdoption"));
        var implementation = graph.AddNode("implementation:impl-a", Node("ControlImplementation"));
        var assertion = graph.AddNode("assertion:assert-a", Node("Assertion"));
        var observation = graph.AddNode("observation:obs-a", Node("Observation"));

        graph.AddEdge("satisfies", adoption, requirement);
        graph.AddEdge("implemented-by", adoption, implementation);
        graph.AddEdge("validates", assertion, implementation);
        graph.AddEdge("produces", assertion, observation);
        graph.AddEdge("validates", assertion, adoption); // the semantically wrong shortcut

        return graph;
    }

    private static bool AssertIs(TraversalPath path, params string[] stepNames) =>
        path.Steps.Select(step => step.Name).SequenceEqual(stepNames);

    private static bool PathIs(TraversalPath path, params string[] nodeIds) =>
        path.Nodes.Select(node => node.Id).SequenceEqual(nodeIds);

    private static TypedPropertyGraph EmptyGraph() => new(EdgeTypeRegistry.Slice);

    private static TypedPropertyGraph LoadDeclaredGraph()
    {
        var graph = EmptyGraph();
        DeclaredRecordsLoader.ApplyAll(graph, RepositoryDataLoader.Load(TestWorld.RepositoryRoot).Records);
        return graph;
    }

    private static NodeType Node(string name) =>
        EdgeTypeRegistry.Slice.NodeTypes.Single(type => type.Name == name);

    private static EdgeType Edge(string forwardName, string inverseName, string fromType, string toType) =>
        EdgeTypeRegistry.Slice.EdgeTypes.Single(edgeType =>
            edgeType.ForwardName == forwardName
            && edgeType.InverseName == inverseName
            && edgeType.FromType.Name == fromType
            && edgeType.ToType.Name == toType);
}
