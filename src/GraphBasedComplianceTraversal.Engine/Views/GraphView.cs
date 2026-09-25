using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Traversal;

namespace GraphBasedComplianceTraversal.Engine.Views;

/// <summary>
/// A named projection of the shared graph: one domain view — architecture,
/// security, or compliance — over the same underlying nodes. A view is a
/// projection function over the shared graph, never a store of its own: it
/// holds the subject node and the <see cref="TraversalPath"/> results it is
/// projected from, and every node it references is the graph's own
/// <see cref="GraphNode"/> instance. No view copies facts, so no view can
/// drift from the graph or from the other views built over it. A view whose
/// paths reach observations additionally carries their current state as
/// <see cref="ViewObservation"/> entries — the compliance view's
/// observations keep their recorded <c>FAIL</c> and their outdated-validator
/// <c>STALE</c> state instead of being smoothed into a blanket success.
/// </summary>
public sealed class GraphView
{
    internal GraphView(
        string name,
        GraphNode subject,
        IReadOnlyList<TraversalPath> paths,
        IReadOnlyList<ViewObservation> observations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(observations);
        Name = name;
        Subject = subject;
        Paths = paths;
        Observations = observations;
        Nodes = paths
            .SelectMany(path => path.Nodes)
            .DistinctBy(node => node.Id)
            .ToArray();
    }

    /// <summary>The view's name: "architecture", "security", or "compliance".</summary>
    public string Name { get; }

    /// <summary>The shared node the view is projected from.</summary>
    public GraphNode Subject { get; }

    /// <summary>The traversal paths the view is projected from, in deterministic traversal order.</summary>
    public IReadOnlyList<TraversalPath> Paths { get; }

    /// <summary>
    /// The distinct nodes the view's paths reference, in first-seen order —
    /// the graph's own instances, never copies, so a node shared between two
    /// views is the same object the graph stores.
    /// </summary>
    public IReadOnlyList<GraphNode> Nodes { get; }

    /// <summary>
    /// The current observation state the view carries, in path order. Empty
    /// for a view whose paths reach no observations.
    /// </summary>
    public IReadOnlyList<ViewObservation> Observations { get; }
}
