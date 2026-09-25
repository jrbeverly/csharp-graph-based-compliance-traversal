using GraphBasedComplianceTraversal.Engine.Graph;

namespace GraphBasedComplianceTraversal.Engine.Assertions;

/// <summary>
/// Selects subjects through an arbitrary graph query — the selector for
/// classes not reducible to a single node type, such as "every production
/// resource", which spans the cloud resource node types. The query receives
/// the whole graph and returns the subjects in evaluation order; it must
/// return each subject at most once.
/// </summary>
public sealed class QuerySubjectSelector : IAssertionSubjectSelector
{
    /// <summary>Creates a selector from a graph query.</summary>
    public QuerySubjectSelector(Func<TypedPropertyGraph, IReadOnlyList<GraphNode>> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        Query = query;
    }

    /// <summary>The graph query producing the selected subjects.</summary>
    public Func<TypedPropertyGraph, IReadOnlyList<GraphNode>> Query { get; }

    /// <inheritdoc/>
    public IReadOnlyList<GraphNode> SelectSubjects(TypedPropertyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return Query(graph);
    }
}
