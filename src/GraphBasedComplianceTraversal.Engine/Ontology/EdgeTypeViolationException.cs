namespace GraphBasedComplianceTraversal.Engine.Ontology;

/// <summary>
/// Thrown when an edge's name or endpoint node types are not declared by the
/// ontology slice. Edge names are directional, so using a name across swapped
/// or undeclared endpoints is a violation even when both endpoint node types
/// are individually declared.
/// </summary>
public sealed class EdgeTypeViolationException : Exception
{
    public EdgeTypeViolationException(string name, NodeType fromType, NodeType toType, string message)
        : base(message)
    {
        Name = name;
        FromType = fromType;
        ToType = toType;
    }

    /// <summary>The edge name (forward or inverse) that failed to resolve.</summary>
    public string Name { get; }

    /// <summary>The node type the edge was declared from.</summary>
    public NodeType FromType { get; }

    /// <summary>The node type the edge was declared to.</summary>
    public NodeType ToType { get; }
}
