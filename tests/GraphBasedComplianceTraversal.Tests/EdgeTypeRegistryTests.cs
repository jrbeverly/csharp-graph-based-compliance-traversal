using GraphBasedComplianceTraversal.Engine.Ontology;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

public sealed class EdgeTypeRegistryTests
{
    [Fact]
    public void SliceDeclaresEveryConceptNamedInThePlanningInput()
    {
        var names = EdgeTypeRegistry.Slice.NodeTypes.Select(type => type.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("BusinessPurpose", names);
        Assert.Contains("Product", names);
        Assert.Contains("ProductCapability", names);
        Assert.Contains("Service", names);
        Assert.Contains("CloudFrontDistribution", names); // technical/cloud resource
        Assert.Contains("S3Bucket", names);               // technical/cloud resource
        Assert.Contains("SourceRepository", names);
        Assert.Contains("BuildPipeline", names);
        Assert.Contains("Artifact", names);
        Assert.Contains("Supplier", names);
        Assert.Contains("Risk", names);
        Assert.Contains("BusinessImpact", names);
        Assert.Contains("Assumption", names);
        Assert.Contains("ControlDefinition", names);
        Assert.Contains("ControlAdoption", names);
        Assert.Contains("ControlImplementation", names);
        Assert.Contains("Assertion", names);
        Assert.Contains("Observation", names);
        Assert.Contains("RegulatoryFramework", names);
        Assert.Contains("RegulatoryRequirement", names);
    }

    [Fact]
    public void SliceDeclaresTheExampleCanonicalEdgeNamesAndFixtureFieldNames()
    {
        var registry = EdgeTypeRegistry.Slice;

        var edgeNames = registry.EdgeTypes
            .SelectMany(edge => new[] { edge.ForwardName, edge.InverseName })
            .ToHashSet(StringComparer.Ordinal);

        foreach (var name in new[]
        {
            "has-capability", "implemented-by", "depends-on", "produced-by", "derived-from",
            "mitigates", "validated-by", "produces", "satisfies", "results-in", "stored-in", "distributed-via",
        })
        {
            Assert.Contains(name, edgeNames);
        }

        var fieldPaths = registry.FieldMappings.Select(mapping => mapping.FieldPath).ToHashSet(StringComparer.Ordinal);
        foreach (var field in new[] { "implemented_by", "built_by", "results_in", "mitigated_by", "satisfied_by" })
        {
            Assert.Contains(field, fieldPaths);
        }
    }

    [Fact]
    public void SliceHasExactlyTheDeclaredShape()
    {
        // Pins the authoritative list; grow these numbers only by extending the
        // registry and updating docs/ontology.md together.
        Assert.Equal(23, EdgeTypeRegistry.Slice.NodeTypes.Count);
        Assert.Equal(35, EdgeTypeRegistry.Slice.EdgeTypes.Count);
        Assert.Equal(49, EdgeTypeRegistry.Slice.FieldMappings.Count);
        Assert.Equal(17, EdgeTypeRegistry.Slice.UnmodeledRelationships.Count);
    }

    [Fact]
    public void ResolvingAForwardNameReturnsTheForwardDirection()
    {
        var registry = EdgeTypeRegistry.Slice;

        var resolved = registry.Resolve("has-capability", Node("Product"), Node("ProductCapability"));

        Assert.Equal(EdgeDirection.Forward, resolved.Direction);
        Assert.Equal("has-capability", resolved.EdgeType.ForwardName);
        Assert.Equal("belongs-to", resolved.EdgeType.InverseName);
        Assert.Equal("Product", resolved.EdgeType.FromType.Name);
        Assert.Equal("ProductCapability", resolved.EdgeType.ToType.Name);
    }

    [Fact]
    public void ResolvingAnInverseNameReturnsTheInverseDirection()
    {
        var registry = EdgeTypeRegistry.Slice;

        var resolved = registry.Resolve("implements", Node("ControlImplementation"), Node("ControlAdoption"));

        Assert.Equal(EdgeDirection.Inverse, resolved.Direction);
        Assert.Equal("implemented-by", resolved.EdgeType.ForwardName);
    }

    [Fact]
    public void SharedEdgeNamesAreDisambiguatedByEndpointTypes()
    {
        var registry = EdgeTypeRegistry.Slice;

        var toBucket = registry.Resolve("depends-on", Node("Service"), Node("S3Bucket"));
        var toDistribution = registry.Resolve("depends-on", Node("Service"), Node("CloudFrontDistribution"));

        Assert.NotSame(toBucket.EdgeType, toDistribution.EdgeType);
        Assert.Equal("used-by", toBucket.EdgeType.InverseName);
        Assert.Equal("serves", toDistribution.EdgeType.InverseName);
    }

    [Fact]
    public void RejectsAnEdgeWhoseEndpointTypeIsNotDeclared()
    {
        var registry = EdgeTypeRegistry.Slice;

        var exception = Assert.Throws<EdgeTypeViolationException>(
            () => registry.Resolve("mitigates", Node("ControlAdoption"), Node("BusinessImpact")));

        Assert.Equal("mitigates", exception.Name);
        Assert.Equal("ControlAdoption", exception.FromType.Name);
        Assert.Equal("BusinessImpact", exception.ToType.Name);
    }

    [Fact]
    public void RejectsAForwardNameUsedAcrossSwappedEndpoints()
    {
        var registry = EdgeTypeRegistry.Slice;

        // "mitigated-by" is the inverse name across these endpoints; the forward
        // name does not resolve backwards.
        var exception = Assert.Throws<EdgeTypeViolationException>(
            () => registry.Resolve("mitigates", Node("Risk"), Node("ControlAdoption")));

        Assert.Contains("mitigates", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnUndeclaredEdgeName()
    {
        var registry = EdgeTypeRegistry.Slice;

        Assert.Throws<EdgeTypeViolationException>(
            () => registry.Resolve("governed-by", Node("Product"), Node("Risk")));
    }

    [Fact]
    public void RejectsEndpointsWhoseNodeTypesAreNotDeclaredAtAll()
    {
        var registry = EdgeTypeRegistry.Slice;

        var team = new NodeType("Team", "team", "Organizational", "A team.");

        var exception = Assert.Throws<EdgeTypeViolationException>(
            () => registry.Resolve("owned-by", Node("Service"), team));

        Assert.Contains("'Team' is not declared", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryEdgeTypeResolvesInBothDeclaredDirections()
    {
        var registry = EdgeTypeRegistry.Slice;

        foreach (var edge in registry.EdgeTypes)
        {
            var forward = registry.Resolve(edge.ForwardName, edge.FromType, edge.ToType);
            Assert.Equal(EdgeDirection.Forward, forward.Direction);
            Assert.Same(edge, forward.EdgeType);

            var inverse = registry.Resolve(edge.InverseName, edge.ToType, edge.FromType);
            Assert.Equal(EdgeDirection.Inverse, inverse.Direction);
            Assert.Same(edge, inverse.EdgeType);
        }
    }

    [Fact]
    public void EveryFieldMappingResolvesThroughItsCanonicalName()
    {
        var registry = EdgeTypeRegistry.Slice;

        foreach (var mapping in registry.FieldMappings)
        {
            var resolved = registry.Resolve(mapping.CanonicalName, mapping.SourceType, mapping.TargetType);
            Assert.Equal(mapping.Direction, resolved.Direction);
            Assert.Same(mapping.Edge, resolved.EdgeType);
        }
    }

    [Fact]
    public void EveryFieldMappingAndUnmodeledFieldAreDisjoint()
    {
        var registry = EdgeTypeRegistry.Slice;

        var mapped = registry.FieldMappings
            .Select(mapping => (mapping.SourceType.Name, mapping.FieldPath))
            .ToHashSet();

        foreach (var unmodeled in registry.UnmodeledRelationships)
        {
            Assert.DoesNotContain((unmodeled.SourceType.Name, unmodeled.FieldPath), mapped);
        }
    }

    [Fact]
    public void DeclaringTheSameEdgeNameAndEndpointsTwiceIsRejected()
    {
        var product = new NodeType("Product", "product", "Business", "A product.");
        var capability = new NodeType("ProductCapability", "capability", "Business", "A capability.");
        var edge = new EdgeType("has-capability", "belongs-to", product, capability, "Duplicated.");

        var exception = Assert.Throws<ArgumentException>(
            () => new EdgeTypeRegistry([product, capability], [edge, edge], [], []));

        Assert.Contains("declared more than once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaringTheSameFieldMappingTwiceIsRejected()
    {
        var registry = EdgeTypeRegistry.Slice;
        var product = Node("Product");
        var capability = Node("ProductCapability");
        var edge = registry.Resolve("has-capability", product, capability).EdgeType;
        var mapping = new RelationshipFieldMapping(product, "capabilities", capability, edge, EdgeDirection.Forward);

        var exception = Assert.Throws<ArgumentException>(
            () => new EdgeTypeRegistry([product, capability], [edge], [mapping, mapping], []));

        Assert.Contains("declared more than once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldMappingInconsistentWithItsEdgeEndpointsIsRejected()
    {
        var product = new NodeType("Product", "product", "Business", "A product.");
        var capability = new NodeType("ProductCapability", "capability", "Business", "A capability.");
        var service = new NodeType("Service", "service", "Technical", "A service.");
        var edge = new EdgeType("has-capability", "belongs-to", product, capability, "A product has a capability.");

        // The mapping claims the field runs Service -> ProductCapability, which
        // matches neither declared direction of the edge type.
        var mapping = new RelationshipFieldMapping(service, "capabilities", capability, edge, EdgeDirection.Forward);

        var exception = Assert.Throws<ArgumentException>(
            () => new EdgeTypeRegistry([product, capability, service], [edge], [mapping], []));

        Assert.Contains("not consistent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEdgeReferencingAUndeclaredNodeTypeIsRejected()
    {
        var product = new NodeType("Product", "product", "Business", "A product.");
        var capability = new NodeType("ProductCapability", "capability", "Business", "A capability.");
        var edge = new EdgeType("has-capability", "belongs-to", product, capability, "Fine.");

        var exception = Assert.Throws<ArgumentException>(
            () => new EdgeTypeRegistry([product], [edge], [], []));

        Assert.Contains("'ProductCapability'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldThatIsBothMappedAndUnmodeledIsRejected()
    {
        var registry = EdgeTypeRegistry.Slice;
        var product = Node("Product");
        var capability = Node("ProductCapability");
        var edge = registry.Resolve("has-capability", product, capability).EdgeType;
        var mapping = new RelationshipFieldMapping(product, "capabilities", capability, edge, EdgeDirection.Forward);
        var unmodeled = new UnmodeledRelationship(product, "capabilities", "No longer modeled.");

        var exception = Assert.Throws<ArgumentException>(
            () => new EdgeTypeRegistry([product, capability], [edge], [mapping], [unmodeled]));

        Assert.Contains("both mapped and listed as unmodeled", exception.Message, StringComparison.Ordinal);
    }

    private static NodeType Node(string name) =>
        EdgeTypeRegistry.Slice.NodeTypes.Single(type => type.Name == name);
}
