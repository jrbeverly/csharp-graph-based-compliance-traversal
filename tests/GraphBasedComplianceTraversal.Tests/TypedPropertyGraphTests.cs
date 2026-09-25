using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ontology;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

public sealed class TypedPropertyGraphTests
{
    [Fact]
    public void NodesAreAddedAndLookedUpByIdAndByType()
    {
        var graph = EmptyGraph();

        var bucket = graph.AddNode(
            "aws:s3:prod-release-artifacts",
            Node("S3Bucket"),
            new Dictionary<string, JsonNode?>
            {
                ["encryption"] = JsonNode.Parse("\"aws:kms\""),
                ["region"] = JsonNode.Parse("\"us-east-1\""),
            });
        var service = graph.AddNode("service:release-distribution", Node("Service"));

        Assert.Same(bucket, graph.GetNode("aws:s3:prod-release-artifacts"));
        Assert.True(graph.TryGetNode("service:release-distribution", out var found));
        Assert.Same(service, found);
        Assert.False(graph.TryGetNode("aws:s3:no-such-bucket", out _));

        Assert.Same(bucket, Assert.Single(graph.GetNodes(Node("S3Bucket"))));
        Assert.Same(service, Assert.Single(graph.GetNodes(Node("Service"))));
        Assert.Empty(graph.GetNodes(Node("Artifact")));

        // Node types are canonicalized to the ontology slice's declaration.
        Assert.Same(Node("S3Bucket"), bucket.Type);
        Assert.Equal("aws:kms", bucket.Properties["encryption"]!.GetValue<string>());
    }

    [Fact]
    public void LookingUpAnUnknownNodeIdThrows()
    {
        var graph = EmptyGraph();

        Assert.Throws<KeyNotFoundException>(() => graph.GetNode("risk:nope"));
    }

    [Fact]
    public void AddingANodeWithADuplicateIdIsRejected()
    {
        var graph = EmptyGraph();
        graph.AddNode("aws:s3:prod-release-artifacts", Node("S3Bucket"));

        var exception = Assert.Throws<ArgumentException>(
            () => graph.AddNode("aws:s3:prod-release-artifacts", Node("S3Bucket")));

        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddingANodeOfAUndeclaredTypeIsRejected()
    {
        var graph = EmptyGraph();
        var team = new NodeType("Team", "team", "Organizational", "A team.");

        var exception = Assert.Throws<ArgumentException>(
            () => graph.AddNode("team:platform-engineering", team));

        Assert.Contains("not declared", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AForwardAndAnInverseQueryAnswerTheSameEdge()
    {
        var graph = EmptyGraph();
        var capability = graph.AddNode("capability:software-update-delivery", Node("ProductCapability"));
        var service = graph.AddNode("service:release-distribution", Node("Service"));

        var edge = graph.AddEdge("implemented-by", capability, service);

        var forward = graph.GetEdges(capability.Id, "implemented-by");
        Assert.Same(edge, Assert.Single(forward));
        Assert.Equal(service.Id, forward[0].ToId);

        var inverse = graph.GetEdges(service.Id, "implements");
        Assert.Same(edge, Assert.Single(inverse));
        Assert.Equal(capability.Id, inverse[0].FromId);

        Assert.Equal(EdgeDirection.Forward, edge.Direction);
        Assert.True(edge.IsResolved);
    }

    [Fact]
    public void AnEdgeAnswersTheDirectionATraversalTakesRelativeToItsEdgeType()
    {
        var graph = EmptyGraph();
        var capability = graph.AddNode("capability:software-update-delivery", Node("ProductCapability"));
        var service = graph.AddNode("service:release-distribution", Node("Service"));

        var forward = graph.AddEdge("implemented-by", capability, service);
        Assert.Equal(EdgeDirection.Forward, forward.DirectionFrom(capability.Id));
        Assert.Equal(EdgeDirection.Inverse, forward.DirectionFrom(service.Id));

        // Stored under its inverse name, the same edge type still answers its
        // forward direction from the to-type endpoint.
        var inverse = graph.AddEdge("implements", service, capability);
        Assert.Equal(EdgeDirection.Inverse, inverse.DirectionFrom(service.Id));
        Assert.Equal(EdgeDirection.Forward, inverse.DirectionFrom(capability.Id));

        Assert.Throws<ArgumentException>(() => forward.DirectionFrom("elsewhere:node"));
    }

    [Fact]
    public void EdgesAreLookedUpByEdgeTypeAndWithoutAName()
    {
        var graph = EmptyGraph();
        var service = graph.AddNode("service:release-distribution", Node("Service"));
        var bucket = graph.AddNode("aws:s3:prod-release-artifacts", Node("S3Bucket"));
        var distribution = graph.AddNode("aws:cloudfront:customer-downloads", Node("CloudFrontDistribution"));

        graph.AddEdge("depends-on", service, bucket);
        graph.AddEdge("depends-on", service, distribution);

        Assert.Equal(2, graph.GetEdges(service.Id).Count);

        var dependsOnBucket = EdgeTypeRegistry.Slice.Resolve("depends-on", Node("Service"), Node("S3Bucket")).EdgeType;
        Assert.Equal(bucket.Id, Assert.Single(graph.GetEdges(dependsOnBucket)).ToId);

        var dependsOnDistribution = EdgeTypeRegistry.Slice.Resolve("depends-on", Node("Service"), Node("CloudFrontDistribution")).EdgeType;
        Assert.Equal(distribution.Id, Assert.Single(graph.GetEdges(dependsOnDistribution)).ToId);
    }

    [Fact]
    public void AddingAnEdgeWithTypeIncompatibleEndpointsIsRejectedWithAClearError()
    {
        var graph = EmptyGraph();
        var adoption = graph.AddNode("control-adoption:ca-artifact-supply-chain", Node("ControlAdoption"));
        var impact = graph.AddNode("impact:artifact-compromise", Node("BusinessImpact"));

        // "mitigates" connects a control adoption to a risk, not to an impact.
        var exception = Assert.Throws<EdgeTypeViolationException>(
            () => graph.AddEdge("mitigates", adoption, impact));

        Assert.Equal("mitigates", exception.Name);
        Assert.Equal("ControlAdoption", exception.FromType.Name);
        Assert.Equal("BusinessImpact", exception.ToType.Name);
        Assert.Contains("directional", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AForwardNameUsedAcrossSwappedEndpointsIsRejected()
    {
        var graph = EmptyGraph();
        var capability = graph.AddNode("capability:software-update-delivery", Node("ProductCapability"));
        var service = graph.AddNode("service:release-distribution", Node("Service"));

        // "implements" reads service -> capability; using it the other way is a violation.
        var exception = Assert.Throws<EdgeTypeViolationException>(
            () => graph.AddEdge("implements", capability, service));

        Assert.Contains("directional", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEdgeToAMissingNodeIdIsRetainedAsAnUnresolvedReference()
    {
        var graph = EmptyGraph();
        var observation = graph.AddNode("observation:obs-2026-08-07-001", Node("Observation"));
        var missingArtifactId = "artifact:customer-agent-2.8.3";

        var edge = graph.AddEdge("observes", observation.Id, observation.Type, missingArtifactId, Node("Artifact"));

        // The forward query from the stored node finds it, and the inverse
        // query from the missing id surfaces it too.
        Assert.Same(edge, Assert.Single(graph.GetEdges(observation.Id, "observes")));
        Assert.Same(edge, Assert.Single(graph.GetEdges(missingArtifactId, "observed-by")));

        Assert.False(graph.TryGetNode(missingArtifactId, out _));
        Assert.False(edge.IsResolved);
        Assert.Equal([missingArtifactId], edge.MissingEndpointIds);
        Assert.Same(edge, Assert.Single(graph.GetUnresolvedEdges()));
    }

    [Fact]
    public void AnEdgeFromAMissingNodeIdIsAlsoRetained()
    {
        var graph = EmptyGraph();
        var artifact = graph.AddNode("artifact:customer-agent-2.8.4", Node("Artifact"));
        var missingObservationId = "observation:obs-2026-08-07-001";

        var edge = graph.AddEdge("observes", missingObservationId, Node("Observation"), artifact.Id, artifact.Type);

        Assert.Same(edge, Assert.Single(graph.GetEdges(artifact.Id, "observed-by")));
        Assert.False(edge.IsResolved);
        Assert.Equal([missingObservationId], edge.MissingEndpointIds);
    }

    [Fact]
    public void AddingTheMissingNodeLaterResolvesTheEdge()
    {
        var graph = EmptyGraph();
        var observation = graph.AddNode("observation:obs-2026-08-07-001", Node("Observation"));
        var missingArtifactId = "artifact:customer-agent-2.8.3";

        var edge = graph.AddEdge("observes", observation.Id, observation.Type, missingArtifactId, Node("Artifact"));
        Assert.False(edge.IsResolved);

        graph.AddNode(missingArtifactId, Node("Artifact"));

        Assert.True(edge.IsResolved);
        Assert.Empty(edge.MissingEndpointIds);
        Assert.Empty(graph.GetUnresolvedEdges());
    }

    [Fact]
    public void AddingAnEdgeByBareIdsRequiresBothNodesAndPointsAtTheExplicitOverload()
    {
        var graph = EmptyGraph();
        var observation = graph.AddNode("observation:obs-2026-08-07-001", Node("Observation"));
        var missingArtifactId = "artifact:customer-agent-2.8.3";

        var exception = Assert.Throws<ArgumentException>(
            () => graph.AddEdge("observes", observation.Id, missingArtifactId));

        Assert.Contains("explicit node types", exception.Message, StringComparison.Ordinal);
        Assert.Equal("toId", exception.ParamName);
    }

    [Fact]
    public void AnExplicitEndpointTypeContradictingTheStoredNodeIsRejected()
    {
        var graph = EmptyGraph();
        var service = graph.AddNode("service:release-distribution", Node("Service"));
        var bucket = graph.AddNode("aws:s3:prod-release-artifacts", Node("S3Bucket"));

        // The bucket is stored as an S3Bucket; claiming CloudFrontDistribution is a contradiction.
        var exception = Assert.Throws<ArgumentException>(
            () => graph.AddEdge("depends-on", service.Id, service.Type, bucket.Id, Node("CloudFrontDistribution")));

        Assert.Contains("already exists with type 'S3Bucket'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFixtureUnmodeledReferencesCanBeRecordedAndQueried()
    {
        var graph = EmptyGraph();
        var risk = graph.AddNode("risk:artifact-tampering", Node("Risk"));
        var repository = graph.AddNode("repository:customer-agent", Node("SourceRepository"));
        var adoption = graph.AddNode("control-adoption:ca-artifact-supply-chain", Node("ControlAdoption"));

        var genericRisk = graph.RecordUnmodeledReference(risk.Id, "generic_risk", "ARR-RISK-044");
        graph.RecordUnmodeledReference(repository.Id, "owner", "team:platform-engineering");
        graph.RecordUnmodeledReference(adoption.Id, "owner", "role:platform-security");
        graph.RecordUnmodeledReference(adoption.Id, "approver", "role:CTO");

        Assert.Equal(4, graph.UnmodeledReferences.Count);
        Assert.Equal("ARR-RISK-044", genericRisk.TargetId);
        Assert.Contains("shared risk library", genericRisk.Reason, StringComparison.Ordinal);
        Assert.Equal(2, graph.GetUnmodeledReferences(adoption.Id).Count);
        Assert.Same(genericRisk, Assert.Single(graph.GetUnmodeledReferences(risk.Id)));
    }

    [Fact]
    public void RecordingAReferenceForAMappedFieldIsRejected()
    {
        var graph = EmptyGraph();
        var risk = graph.AddNode("risk:artifact-tampering", Node("Risk"));

        var exception = Assert.Throws<ArgumentException>(
            () => graph.RecordUnmodeledReference(risk.Id, "affects", "product:customer-agent"));

        Assert.Contains("mapped to a typed edge", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordingAReferenceForAnUndeclaredFieldIsRejected()
    {
        var graph = EmptyGraph();
        var risk = graph.AddNode("risk:artifact-tampering", Node("Risk"));

        var exception = Assert.Throws<ArgumentException>(
            () => graph.RecordUnmodeledReference(risk.Id, "frobnicates", "product:customer-agent"));

        Assert.Contains("not declared as an unmodeled relationship", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordingAReferenceFromAnUnknownNodeIsRejected()
    {
        var graph = EmptyGraph();

        var exception = Assert.Throws<ArgumentException>(
            () => graph.RecordUnmodeledReference("risk:artifact-tampering", "generic_risk", "ARR-RISK-044"));

        Assert.Contains("is not in the graph", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRegistryEdgeTypeCanBeStoredAndQueriedInBothDirections()
    {
        var graph = EmptyGraph();
        var probe = 0;

        foreach (var edgeType in EdgeTypeRegistry.Slice.EdgeTypes)
        {
            // Forward: added under the forward name, answered forward and inverse.
            var forwardFrom = graph.AddNode($"{edgeType.FromType.IdPrefix}:probe-f-{probe}", edgeType.FromType);
            var forwardTo = graph.AddNode($"{edgeType.ToType.IdPrefix}:probe-f-{probe}", edgeType.ToType);
            var forward = graph.AddEdge(edgeType.ForwardName, forwardFrom, forwardTo);
            Assert.Same(forward, Assert.Single(graph.GetEdges(forwardFrom.Id, edgeType.ForwardName)));
            Assert.Same(forward, Assert.Single(graph.GetEdges(forwardTo.Id, edgeType.InverseName)));

            // Inverse: added under the inverse name, still answered both ways.
            var inverseFrom = graph.AddNode($"{edgeType.ToType.IdPrefix}:probe-i-{probe}", edgeType.ToType);
            var inverseTo = graph.AddNode($"{edgeType.FromType.IdPrefix}:probe-i-{probe}", edgeType.FromType);
            var inverse = graph.AddEdge(edgeType.InverseName, inverseFrom, inverseTo);
            Assert.Same(inverse, Assert.Single(graph.GetEdges(inverseFrom.Id, edgeType.InverseName)));
            Assert.Same(inverse, Assert.Single(graph.GetEdges(inverseTo.Id, edgeType.ForwardName)));

            probe++;
        }
    }

    [Fact]
    public void AFixtureShapedWorldIsConstructedAndWalkedInBothDirections()
    {
        var graph = EmptyGraph();
        var purpose = graph.AddNode("business-purpose:secure-endpoint-management", Node("BusinessPurpose"));
        var product = graph.AddNode("product:customer-agent", Node("Product"));
        var capability = graph.AddNode("capability:software-update-delivery", Node("ProductCapability"));
        var service = graph.AddNode("service:release-distribution", Node("Service"));
        var repository = graph.AddNode("repository:customer-agent", Node("SourceRepository"));
        var pipeline = graph.AddNode("build-pipeline:customer-agent-release", Node("BuildPipeline"));
        var artifact = graph.AddNode("artifact:customer-agent-2.8.4", Node("Artifact"));
        var bucket = graph.AddNode("aws:s3:prod-release-artifacts", Node("S3Bucket"));
        var distribution = graph.AddNode("aws:cloudfront:customer-downloads", Node("CloudFrontDistribution"));
        var supplier = graph.AddNode("supplier:aws", Node("Supplier"));
        var risk = graph.AddNode("risk:artifact-tampering", Node("Risk"));
        var impact = graph.AddNode("impact:artifact-compromise", Node("BusinessImpact"));
        var assumption = graph.AddNode("assumption:customer-endpoints-managed", Node("Assumption"));
        var definition = graph.AddNode("control-def:arr-3-11", Node("ControlDefinition"));
        var adoption = graph.AddNode("control-adoption:ca-artifact-supply-chain", Node("ControlAdoption"));
        var implementation = graph.AddNode("implementation:signed-release-pipeline", Node("ControlImplementation"));
        var exception = graph.AddNode("exception:emergency-deployment-path", Node("ControlException"));
        var assertion = graph.AddNode("assertion:prod-artifact-provenance", Node("Assertion"));
        var observation = graph.AddNode("observation:obs-2026-08-08-001", Node("Observation"));
        var framework = graph.AddNode("framework:nis2", Node("RegulatoryFramework"));
        var requirement = graph.AddNode("requirement:nis2-article-21-supply-chain", Node("RegulatoryRequirement"));

        graph.AddEdge("fulfills", product, purpose);
        graph.AddEdge("has-capability", product, capability);
        graph.AddEdge("implemented-by", capability, service);
        graph.AddEdge("produced-by", artifact, pipeline);
        graph.AddEdge("stored-in", artifact, bucket);
        graph.AddEdge("distributed-via", artifact, distribution);
        graph.AddEdge("source", pipeline, repository);
        graph.AddEdge("depends-on", service, bucket);
        graph.AddEdge("depends-on", service, distribution);
        graph.AddEdge("origin", distribution, bucket);
        graph.AddEdge("supplies", supplier, service);
        graph.AddEdge("affects", risk, product);
        graph.AddEdge("results-in", risk, impact);
        graph.AddEdge("mitigates", adoption, risk);
        graph.AddEdge("informs", impact, adoption);
        graph.AddEdge("supports", assumption, risk);
        graph.AddEdge("supports", assumption, impact);
        graph.AddEdge("derived-from", adoption, definition);
        graph.AddEdge("applies-to", adoption, pipeline);
        graph.AddEdge("applies-to", adoption, artifact);
        graph.AddEdge("applies-to", adoption, bucket);
        graph.AddEdge("applies-to", adoption, distribution);
        graph.AddEdge("applies-to", exception, service);
        graph.AddEdge("applies-to", exception, bucket);
        graph.AddEdge("exception-to", exception, adoption);
        graph.AddEdge("implemented-by", adoption, implementation);
        graph.AddEdge("validates", assertion, adoption);
        graph.AddEdge("validates", assertion, implementation);
        graph.AddEdge("produces", assertion, observation);
        graph.AddEdge("observes", observation, artifact);
        graph.AddEdge("requires", framework, requirement);
        graph.AddEdge("satisfies", adoption, requirement);

        // The fixture's dangling references: a capability the product references
        // without a record, and an observation of an artifact version that has none.
        var missingCapabilityId = "capability:endpoint-telemetry-collection";
        graph.AddEdge("has-capability", product.Id, product.Type, missingCapabilityId, Node("ProductCapability"));
        var missingArtifactId = "artifact:customer-agent-2.8.3";
        graph.AddEdge("observes", observation.Id, observation.Type, missingArtifactId, Node("Artifact"));

        // The fixture's unmodeled references: a shared-library risk and owners
        // pointing at teams and roles, which are not node types in this slice.
        graph.RecordUnmodeledReference(risk.Id, "generic_risk", "ARR-RISK-044");
        graph.RecordUnmodeledReference(repository.Id, "owner", "team:platform-engineering");
        graph.RecordUnmodeledReference(adoption.Id, "owner", "role:platform-security");
        graph.RecordUnmodeledReference(adoption.Id, "approver", "role:CTO");

        // Forward walk: risk -> adoption -> implementation -> assertion ->
        // observation -> artifact -> bucket.
        Assert.Equal([adoption.Id], Walk(graph, risk.Id, "mitigated-by"));
        Assert.Equal([implementation.Id], Walk(graph, adoption.Id, "implemented-by"));
        Assert.Equal([assertion.Id], Walk(graph, implementation.Id, "validated-by"));
        Assert.Equal([observation.Id], Walk(graph, assertion.Id, "produces"));
        // The walk from the observation answers the stored artifact and the
        // dangling reference to the artifact version that has no record.
        Assert.Equal([artifact.Id, missingArtifactId], Walk(graph, observation.Id, "observes"));
        Assert.Equal([bucket.Id], Walk(graph, artifact.Id, "stored-in"));

        // Reverse walk: requirement -> adoption -> risk -> product -> purpose,
        // and bucket -> artifact, all through inverse names.
        Assert.Equal([adoption.Id], Walk(graph, requirement.Id, "satisfied-by"));
        Assert.Equal([risk.Id], Walk(graph, adoption.Id, "mitigates"));
        Assert.Equal([product.Id], Walk(graph, risk.Id, "affects"));
        Assert.Equal([purpose.Id], Walk(graph, product.Id, "fulfills"));
        Assert.Equal([artifact.Id], Walk(graph, bucket.Id, "contains"));

        // The dangling edges and the unmodeled references are represented, not hidden.
        Assert.Equal(2, graph.GetUnresolvedEdges().Count);
        Assert.Equal(4, graph.UnmodeledReferences.Count);
        Assert.Equal("ARR-RISK-044", Assert.Single(graph.GetUnmodeledReferences(risk.Id)).TargetId);
    }

    private static TypedPropertyGraph EmptyGraph() => new(EdgeTypeRegistry.Slice);

    private static NodeType Node(string name) =>
        EdgeTypeRegistry.Slice.NodeTypes.Single(type => type.Name == name);

    private static string[] Walk(TypedPropertyGraph graph, string fromId, string name) =>
        graph.GetEdges(fromId, name)
            .Select(edge => edge.ToId == fromId ? edge.FromId : edge.ToId)
            .ToArray();
}
