using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ontology;

namespace GraphBasedComplianceTraversal.Engine.Traversal;

/// <summary>
/// A typed, directional path query over a <see cref="TypedPropertyGraph"/>: a
/// start node and an ordered chain of named edge steps. <see cref="Execute"/>
/// walks the graph step by step and returns the <see cref="TraversalPath"/>
/// results — deterministic, cycle-safe, and carrying the full trail of typed
/// steps rather than just their endpoints.
/// <list type="bullet">
/// <item>A step names an edge by its forward or its inverse name
/// (<see cref="Step(string)"/>): every edge reading the name from the current
/// node is followed, in whichever direction of the edge type that is.</item>
/// <item>A step may constrain the direction
/// (<see cref="Step(string, EdgeDirection)"/>):
/// <see cref="EdgeDirection.Forward"/> traverses the named edge type from its
/// from-type endpoint to its to-type endpoint, and
/// <see cref="EdgeDirection.Inverse"/> traverses it back — so
/// <c>Step("depends-on", EdgeDirection.Inverse)</c> from a bucket reaches the
/// service that depends on it, exactly like <c>Step("used-by")</c>.</item>
/// <item>Steps chain; at each step every route fans out over the matching
/// edges in the graph's insertion order, so results are deterministic.</item>
/// <item>A step onto a node already on the path closes a loop: the path is
/// reported with <see cref="TraversalPath.IsCycle"/> true on the query's
/// final step, and the loop is never extended — a traversal over a cycle
/// therefore terminates and reports the repeated node rather than
/// looping.</item>
/// <item>An edge whose reached endpoint has no stored node is not followed: a
/// path is a sequence of nodes, and the missing endpoint is not one. Such
/// unresolved edges stay queryable through the graph itself, and
/// <see cref="ExecuteWithGaps"/> reports each one the walk hit as a
/// <see cref="TraversalGap"/>, so an incomplete walk is visible instead of
/// reading as a complete one that matched nothing.</item>
/// </list>
/// A step name the ontology slice does not declare is rejected when the step
/// is added, and a start node that is not in the graph is rejected at
/// construction — a mistyped query never silently reports nothing.
/// </summary>
public sealed class GraphTraversal
{
    private readonly TypedPropertyGraph _graph;
    private readonly string _startId;
    private readonly QueryStep[] _steps;

    /// <summary>
    /// Creates a query that starts from the node <paramref name="startId"/>,
    /// which must be stored in <paramref name="graph"/>.
    /// </summary>
    public GraphTraversal(TypedPropertyGraph graph, string startId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(startId);
        _graph = graph;
        _startId = graph.GetNode(startId).Id;
        _steps = [];
    }

    /// <summary>Creates a query that starts from <paramref name="start"/>.</summary>
    public GraphTraversal(TypedPropertyGraph graph, GraphNode start)
        : this(graph, (start ?? throw new ArgumentNullException(nameof(start))).Id)
    {
    }

    private GraphTraversal(TypedPropertyGraph graph, string startId, QueryStep[] steps)
    {
        _graph = graph;
        _startId = startId;
        _steps = steps;
    }

    /// <summary>The graph the query executes against.</summary>
    public TypedPropertyGraph Graph => _graph;

    /// <summary>The id of the node the traversal starts from.</summary>
    public string StartId => _startId;

    /// <summary>
    /// Appends a step that follows every edge reading <paramref name="name"/>
    /// from the current node — a forward or an inverse name alike — and
    /// returns a new query with the step chained. The name must be declared by
    /// the graph's ontology slice.
    /// </summary>
    public GraphTraversal Step(string name) => With(name, direction: null);

    /// <summary>
    /// Appends a step that traverses the edge type(s) declaring
    /// <paramref name="name"/> (as their forward or their inverse name) in the
    /// given direction — <see cref="EdgeDirection.Forward"/> from the edge
    /// type's from-type endpoint to its to-type endpoint,
    /// <see cref="EdgeDirection.Inverse"/> back along it — and returns a new
    /// query with the step chained. The name must be declared by the graph's
    /// ontology slice.
    /// </summary>
    public GraphTraversal Step(string name, EdgeDirection direction) => With(name, direction);

    /// <summary>
    /// Executes the query: walks the graph step by step from the start node
    /// and returns one <see cref="TraversalPath"/> per route the steps
    /// follow. Ordering is deterministic — routes fan out over the matching
    /// edges in the graph's insertion order at every step — and execution
    /// always terminates: a step onto a node already on the path closes the
    /// loop and is never extended. Executing the same query twice returns
    /// equal paths.
    /// </summary>
    public IReadOnlyList<TraversalPath> Execute() => Walk(collectGaps: false).Paths;

    /// <summary>
    /// Executes the query and, alongside the completed paths, reports the
    /// unresolved references the walk hit: every edge a step matched whose
    /// reached endpoint has no stored node is returned as a
    /// <see cref="TraversalGap"/> — the route ended before it, and the gap
    /// says so instead of the incomplete walk silently reading as a complete
    /// one that simply matched nothing. The paths are exactly those
    /// <see cref="Execute"/> returns; gaps and paths are both ordered in
    /// traversal order.
    /// </summary>
    public TraversalReport ExecuteWithGaps()
    {
        var (paths, gaps) = Walk(collectGaps: true);
        return new TraversalReport(paths, gaps);
    }

    private (IReadOnlyList<TraversalPath> Paths, IReadOnlyList<TraversalGap> Gaps) Walk(bool collectGaps)
    {
        var gaps = new List<TraversalGap>();
        var reportedGaps = new HashSet<(string StepName, string FromNodeId, GraphEdge Edge)>();
        var frontier = new List<TraversalPath> { new(_graph.GetNode(_startId), []) };
        for (var index = 0; index < _steps.Length; index++)
        {
            var isLastStep = index == _steps.Length - 1;
            var next = new List<TraversalPath>();
            foreach (var path in frontier)
            {
                foreach (var edge in MatchingEdges(path.End, _steps[index]))
                {
                    var reachedId = edge.FromId == path.End.Id ? edge.ToId : edge.FromId;
                    if (!_graph.TryGetNode(reachedId, out var reached))
                    {
                        // Unresolved edge: a path of nodes cannot step onto a
                        // missing node, so the route ends here. The gap is
                        // reported once, however many routes passed the same
                        // frontier node.
                        if (collectGaps && reportedGaps.Add((_steps[index].Name, path.End.Id, edge)))
                        {
                            gaps.Add(new TraversalGap(_steps[index].Name, path.End.Id, edge));
                        }

                        continue;
                    }

                    if (path.ContainsNode(reached) && !isLastStep)
                    {
                        // The step closes a loop before the query's final
                        // step: the path cannot revisit a node and still
                        // complete the remaining steps, so this route ends
                        // here.
                        continue;
                    }

                    next.Add(path.Append(new TraversalStep(reached, edge, _steps[index].Name, edge.DirectionFrom(path.End.Id))));
                }
            }

            frontier = next;
        }

        return (frontier, gaps);
    }

    private GraphTraversal With(string name, EdgeDirection? direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!_graph.Registry.EdgeTypes.Any(edgeType =>
            string.Equals(edgeType.ForwardName, name, StringComparison.Ordinal)
            || string.Equals(edgeType.InverseName, name, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"'{name}' is not declared as an edge name by the ontology slice, so it cannot be traversed.",
                nameof(name));
        }

        var steps = new QueryStep[_steps.Length + 1];
        Array.Copy(_steps, steps, _steps.Length);
        steps[^1] = new QueryStep(name, direction);
        return new GraphTraversal(_graph, _startId, steps);
    }

    private IEnumerable<GraphEdge> MatchingEdges(GraphNode from, QueryStep step)
    {
        if (step.Direction is null)
        {
            return _graph.GetEdges(from.Id, step.Name);
        }

        // A direction-constrained step traverses the edge types that declare
        // the name (as their forward or their inverse name) in the requested
        // direction. From the current node each such edge type is read under
        // the name that faces the requested direction — the forward name for a
        // forward step, the inverse name for an inverse step — and matched
        // against the edge's actual traversal direction.
        var results = new List<GraphEdge>();
        foreach (var edgeType in _graph.Registry.EdgeTypes)
        {
            var declaresName = string.Equals(edgeType.ForwardName, step.Name, StringComparison.Ordinal)
                || string.Equals(edgeType.InverseName, step.Name, StringComparison.Ordinal);
            if (!declaresName)
            {
                continue;
            }

            var nameReadFromNode = step.Direction == EdgeDirection.Forward ? edgeType.ForwardName : edgeType.InverseName;
            foreach (var edge in _graph.GetEdges(from.Id, nameReadFromNode))
            {
                if (edge.EdgeType == edgeType && edge.DirectionFrom(from.Id) == step.Direction)
                {
                    results.Add(edge);
                }
            }
        }

        return results;
    }

    private readonly record struct QueryStep(string Name, EdgeDirection? Direction);
}
