using GraphBasedComplianceTraversal.Engine.Ontology;

namespace GraphBasedComplianceTraversal.Engine.Traversal;

/// <summary>
/// The textual explained-path rendering: a traversal path written as the
/// ordered node sequence with the typed edge that connects each pair of
/// adjacent nodes named between them, so the path reads as its own
/// justification — every hop states which edge type was traversed, under
/// which name, in which direction. <see cref="Render(TraversalReport)"/>
/// additionally renders the unresolved references a traversal hit, so an
/// incomplete walk reports its gaps instead of looking complete.
/// </summary>
public static class ExplainedPath
{
    /// <summary>
    /// Renders one path: the start node's id, then one line per step naming
    /// the queried edge name, the reached node's id, the edge type the step
    /// traversed, and the direction it moved in — every pair of adjacent
    /// nodes is joined by the typed edge that connects them.
    /// </summary>
    public static string Render(TraversalPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return string.Join('\n', EnumerateLines(path));
    }

    /// <summary>Renders several paths, separated by blank lines, in the order given.</summary>
    public static string Render(IReadOnlyList<TraversalPath> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return string.Join("\n\n", paths.Select(Render));
    }

    /// <summary>
    /// Renders a traversal report: its completed paths followed by the
    /// unresolved references it hit, each naming the queried step, the
    /// missing endpoint, and the node the edge was matched from. A report
    /// with no completed paths but gaps to report renders the gaps alone —
    /// the walk did not silently complete, and the rendering says where it
    /// stopped and why.
    /// </summary>
    public static string Render(TraversalReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sections = new List<string>();
        if (report.Paths.Count > 0)
        {
            sections.Add(Render(report.Paths));
        }

        if (report.UnresolvedReferences.Count > 0)
        {
            sections.Add(
                "Unresolved references:" +
                string.Join(string.Empty, report.UnresolvedReferences.Select(gap =>
                    $"\n  {gap.StepName} → {string.Join(", ", gap.Edge.MissingEndpointIds)} (from {gap.FromNodeId})")));
        }

        return string.Join("\n\n", sections);
    }

    private static IEnumerable<string> EnumerateLines(TraversalPath path)
    {
        yield return path.Start.Id;
        foreach (var step in path.Steps)
        {
            yield return
                $"  → {step.Name} → {step.Node.Id} [{step.Edge.EdgeType.ForwardName}, {DirectionName(step.Direction)}]";
        }
    }

    private static string DirectionName(EdgeDirection direction) =>
        direction == EdgeDirection.Forward ? "forward" : "inverse";
}
