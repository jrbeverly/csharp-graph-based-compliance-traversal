using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ontology;

namespace GraphBasedComplianceTraversal.Engine.Traversal;

/// <summary>
/// One step of a <see cref="TraversalPath"/>'s trail: the node the step
/// reached, the typed edge it traversed, the edge name the query asked for,
/// and the direction the step moved in relative to the edge type
/// (<see cref="EdgeDirection.Forward"/> along the edge type's declared forward
/// direction, <see cref="EdgeDirection.Inverse"/> back along it). The name and
/// direction together make the step inspectable: <c>used-by</c> from a bucket
/// traverses the same stored edge as <c>depends-on</c> from a service, in the
/// opposite direction.
/// </summary>
public sealed record TraversalStep(
    GraphNode Node,
    GraphEdge Edge,
    string Name,
    EdgeDirection Direction);
