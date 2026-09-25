using GraphBasedComplianceTraversal.Engine.Graph;

namespace GraphBasedComplianceTraversal.Engine.Traversal;

/// <summary>
/// One result of executing a <see cref="GraphTraversal"/>: the start node and
/// the ordered trail of typed steps the traversal took, from the first step
/// out of the start node to the last reached node. The trail carries every
/// step's reached node, traversed edge, queried name, and direction, so a
/// path is inspectable as a whole rather than reduced to its endpoints. A
/// path whose final step revisits a node already on the path is a closed
/// loop: <see cref="IsCycle"/> is true and <see cref="End"/> is the repeated
/// node.
/// </summary>
public sealed class TraversalPath
{
    private readonly TraversalStep[] _steps;
    private readonly GraphNode[] _nodes;

    /// <summary>
    /// Creates a path from a start node and an ordered trail of steps. The
    /// steps are copied, so later mutations of <paramref name="steps"/> do not
    /// touch the path.
    /// </summary>
    public TraversalPath(GraphNode start, IReadOnlyList<TraversalStep> steps)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(steps);
        Start = start;
        _steps = [.. steps];
        _nodes = [start, .. _steps.Select(step => step.Node)];
    }

    /// <summary>The node the traversal started from.</summary>
    public GraphNode Start { get; }

    /// <summary>The ordered trail of typed steps, in traversal order.</summary>
    public IReadOnlyList<TraversalStep> Steps => _steps;

    /// <summary>The ordered nodes of the path, from <see cref="Start"/> through the last reached node.</summary>
    public IReadOnlyList<GraphNode> Nodes => _nodes;

    /// <summary>
    /// The last reached node — <see cref="Start"/> for a path with no steps,
    /// and the repeated node when <see cref="IsCycle"/>.
    /// </summary>
    public GraphNode End => _nodes[^1];

    /// <summary>
    /// True when the final step revisited a node already present earlier in
    /// the path, so the traversal closed a loop. The repeated node is
    /// <see cref="End"/>; the loop is reported, never extended.
    /// </summary>
    public bool IsCycle => Steps.Count > 0 && Nodes.Take(Nodes.Count - 1).Any(node => node == End);

    /// <summary>Returns a new path extended by one step.</summary>
    internal TraversalPath Append(TraversalStep step) => new(Start, [.. _steps, step]);

    /// <summary>Whether the path already visits <paramref name="node"/>.</summary>
    internal bool ContainsNode(GraphNode node) => _nodes.Contains(node);
}
