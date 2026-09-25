using GraphBasedComplianceTraversal.Engine.Graph;

namespace GraphBasedComplianceTraversal.Engine.Traversal;

/// <summary>
/// An unresolved reference a traversal hit: an edge that read the queried
/// step name from a reached node but whose reached endpoint has no stored
/// node, so the route ended before that edge — a path of nodes cannot step
/// onto a missing one. Reporting the gap keeps an incomplete walk visible
/// instead of letting it read as a complete one that simply matched nothing.
/// </summary>
public sealed record TraversalGap(
    string StepName,
    string FromNodeId,
    GraphEdge Edge);

/// <summary>
/// The outcome of executing a <see cref="GraphTraversal"/> with
/// <see cref="GraphTraversal.ExecuteWithGaps"/>: the completed
/// <see cref="TraversalPath"/> results — the same ones
/// <see cref="GraphTraversal.Execute"/> returns — alongside the
/// <see cref="TraversalGap"/> report for every unresolved reference the walk
/// hit on the way. Both lists are ordered deterministically, in traversal
/// order.
/// </summary>
public sealed class TraversalReport
{
    internal TraversalReport(
        IReadOnlyList<TraversalPath> paths,
        IReadOnlyList<TraversalGap> unresolvedReferences)
    {
        Paths = paths;
        UnresolvedReferences = unresolvedReferences;
    }

    /// <summary>
    /// The completed paths, in deterministic fan-out order. Empty when no
    /// route completed every step — whether the steps matched nothing or
    /// every route ended at an unresolved reference.
    /// </summary>
    public IReadOnlyList<TraversalPath> Paths { get; }

    /// <summary>
    /// The unresolved references the walk hit, in traversal order: edges a
    /// step matched whose reached endpoint has no stored node, so the route
    /// ended before them. Each gap is reported once, even when several
    /// routes passed the same frontier node. Empty when the walk hit no
    /// unresolved reference.
    /// </summary>
    public IReadOnlyList<TraversalGap> UnresolvedReferences { get; }
}
