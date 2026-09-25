using GraphBasedComplianceTraversal.Engine.Ontology;

namespace GraphBasedComplianceTraversal.Engine.Traversal;

/// <summary>
/// One permitted step of a <see cref="PathGrammar"/>: an edge type traversed
/// in a declared direction. A step pins the edge type — never merely a name,
/// so edge types that share a name across different endpoint pairs (for
/// example the two <c>implemented-by</c> edge types) stay distinct — and the
/// direction the edge type is traversed in:
/// <see cref="EdgeDirection.Forward"/> from the edge type's from-type
/// endpoint to its to-type endpoint, <see cref="EdgeDirection.Inverse"/> back
/// along it. Because the edge type declares its endpoint node types, the step
/// also pins the node type it enters from and the node type it exits to, so a
/// grammar's steps chain across matching node types by construction.
/// </summary>
public sealed record PathGrammarStep(EdgeType EdgeType, EdgeDirection Direction)
{
    /// <summary>The edge name the step reads along the path: the edge type's forward name for a forward step, its inverse name for an inverse step.</summary>
    public string Name => Direction == EdgeDirection.Forward ? EdgeType.ForwardName : EdgeType.InverseName;

    /// <summary>The node type the step enters from: the edge type's from-type endpoint for a forward step, its to-type endpoint for an inverse step.</summary>
    public NodeType FromType => Direction == EdgeDirection.Forward ? EdgeType.FromType : EdgeType.ToType;

    /// <summary>The node type the step exits to: the edge type's to-type endpoint for a forward step, its from-type endpoint for an inverse step.</summary>
    public NodeType ToType => Direction == EdgeDirection.Forward ? EdgeType.ToType : EdgeType.FromType;
}
