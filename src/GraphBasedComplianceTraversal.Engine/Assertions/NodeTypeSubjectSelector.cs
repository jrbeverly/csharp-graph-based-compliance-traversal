using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ontology;

namespace GraphBasedComplianceTraversal.Engine.Assertions;

/// <summary>
/// Selects every node of one declared node type — the "class/type" subject
/// selector ("every production artifact", "every adopted control") — with an
/// optional filter narrowing the class (for example to the subjects
/// classified production). Subjects appear in graph insertion order, which
/// keeps evaluation deterministic.
/// </summary>
public sealed class NodeTypeSubjectSelector : IAssertionSubjectSelector
{
    /// <summary>
    /// Creates a selector for every node of <paramref name="type"/>,
    /// optionally narrowed by <paramref name="filter"/>.
    /// </summary>
    public NodeTypeSubjectSelector(NodeType type, Func<GraphNode, bool>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        Type = type;
        Filter = filter;
    }

    /// <summary>The node type whose nodes are selected.</summary>
    public NodeType Type { get; }

    /// <summary>The optional class filter; <c>null</c> selects every node of the type.</summary>
    public Func<GraphNode, bool>? Filter { get; }

    /// <inheritdoc/>
    public IReadOnlyList<GraphNode> SelectSubjects(TypedPropertyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return graph.GetNodes(Type)
            .Where(node => Filter is null || Filter(node))
            .ToArray();
    }
}
