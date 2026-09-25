using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ontology;

namespace GraphBasedComplianceTraversal.Engine.Traversal;

/// <summary>
/// A named path grammar: the declaration of the typed edge sequences that
/// constitute a semantically valid path between two kinds of nodes. Where
/// <see cref="GraphTraversal"/> executes any sequence of declared steps, a
/// grammar constrains the sequence itself — a conclusion is only reached
/// through the grammar's permitted edge types in their declared order and
/// direction, never an arbitrary hop between the same endpoints.
/// <list type="bullet">
/// <item>A grammar is a name plus an ordered chain of
/// <see cref="PathGrammarStep"/> values, each an edge type traversed in a
/// direction. The chain is type-checked as it is built: consecutive steps
/// must connect across matching node types, so the assurance path
/// <c>satisfied-by → implemented-by → validated-by → produces</c> is declared
/// step by step from a requirement through a control adoption, an
/// implementation, and an assertion to an observation.</item>
/// <item>Steps may be added by edge type and direction
/// (<see cref="Step(EdgeType, EdgeDirection)"/>) or by edge name with the
/// node types it connects (<see cref="Step(string, NodeType, NodeType)"/>),
/// which the ontology slice resolves to the same pair. A step that is not
/// declared by the slice or that does not chain onto the preceding step is
/// rejected when it is added — a mistyped grammar never silently matches
/// nothing.</item>
/// <item><see cref="Evaluate(TypedPropertyGraph, string)"/> finds every path
/// from a start node that follows the grammar's permitted sequence and, at
/// the same time, surfaces the paths from the same start that reach the
/// grammar's terminal node type through a different sequence as rejected —
/// an <c>addressed</c>/<c>satisfied</c> conclusion always corresponds to a
/// real, semantically valid edge sequence, and when none matches the verdict
/// is an explicit no-valid-path rather than a fabricated one.</item>
/// <item>A requirement may be satisfiable by more than one valid path; every
/// matching path is returned.</item>
/// </list>
/// </summary>
public sealed class PathGrammar
{
    private readonly EdgeTypeRegistry _registry;
    private readonly PathGrammarStep[] _steps;

    /// <summary>
    /// Creates an empty grammar named <paramref name="name"/>, validated
    /// against the ontology slice <paramref name="registry"/> declares. Steps
    /// are chained with <see cref="Step(EdgeType, EdgeDirection)"/> or
    /// <see cref="Step(string, NodeType, NodeType)"/>.
    /// </summary>
    public PathGrammar(EdgeTypeRegistry registry, string name)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _registry = registry;
        Name = name;
        _steps = [];
    }

    private PathGrammar(EdgeTypeRegistry registry, string name, PathGrammarStep[] steps)
    {
        _registry = registry;
        Name = name;
        _steps = steps;
    }

    /// <summary>The ontology slice the grammar's steps are validated against.</summary>
    public EdgeTypeRegistry Registry => _registry;

    /// <summary>The grammar's name.</summary>
    public string Name { get; }

    /// <summary>The permitted steps, in order — empty until the first step is added.</summary>
    public IReadOnlyList<PathGrammarStep> Steps => _steps;

    /// <summary>The node type a matching path starts from — the first step's entry type; <c>null</c> for an empty grammar.</summary>
    public NodeType? StartType => _steps.Length == 0 ? null : _steps[0].FromType;

    /// <summary>The node type a matching path ends at — the last step's exit type; <c>null</c> for an empty grammar.</summary>
    public NodeType? EndType => _steps.Length == 0 ? null : _steps[^1].ToType;

    /// <summary>
    /// Appends a step permitting <paramref name="edgeType"/> traversed in
    /// <paramref name="direction"/> and returns a new grammar with the step
    /// chained. The edge type must be declared by the grammar's ontology
    /// slice, and the step must enter from the node type the preceding step
    /// exits to.
    /// </summary>
    public PathGrammar Step(EdgeType edgeType, EdgeDirection direction)
    {
        ArgumentNullException.ThrowIfNull(edgeType);

        if (!_registry.EdgeTypes.Contains(edgeType))
        {
            throw new ArgumentException(
                $"Edge type '{edgeType.ForwardName}' → '{edgeType.InverseName}' is not declared by the ontology slice, so it cannot be a grammar step.",
                nameof(edgeType));
        }

        var step = new PathGrammarStep(edgeType, direction);
        if (_steps.Length > 0 && _steps[^1].ToType.Name != step.FromType.Name)
        {
            throw new ArgumentException(
                $"Step '{step.Name}' enters from node type '{step.FromType.Name}', but the grammar's preceding step exits to '{_steps[^1].ToType.Name}'. " +
                "Consecutive grammar steps must chain across matching node types.",
                nameof(edgeType));
        }

        return Append(step);
    }

    /// <summary>
    /// Appends a step permitting the edge reading <paramref name="name"/> from
    /// node type <paramref name="fromType"/> to node type
    /// <paramref name="toType"/> and returns a new grammar with the step
    /// chained. The ontology slice resolves the name across the endpoint types
    /// to one declared edge type and direction — the <c>satisfied-by</c> step
    /// from a requirement to a control adoption is the <c>satisfies</c> edge
    /// type traversed inversely — and the resolution is checked to chain onto
    /// the preceding step.
    /// </summary>
    public PathGrammar Step(string name, NodeType fromType, NodeType toType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(fromType);
        ArgumentNullException.ThrowIfNull(toType);

        // Resolve rejects a name or endpoint pair the slice does not declare.
        var resolved = _registry.Resolve(name, fromType, toType);
        return Step(resolved.EdgeType, resolved.Direction);
    }

    /// <summary>
    /// Evaluates the grammar from the node <paramref name="start"/>: returns
    /// a <see cref="PathGrammarResult"/> carrying every path that followed the
    /// grammar's permitted edge sequence and every path from the same start
    /// that reached the grammar's terminal node type through a different
    /// sequence — the valid and the rejected side by side. When no path
    /// matches, the result's verdict is <see cref="PathGrammarVerdict.NoValidPath"/>:
    /// explicit, never a fabricated path. A start node whose type is not the
    /// grammar's start type simply yields no valid path — the grammar does not
    /// govern that node.
    /// </summary>
    public PathGrammarResult Evaluate(TypedPropertyGraph graph, GraphNode start)
    {
        ArgumentNullException.ThrowIfNull(start);
        return Evaluate(graph, start.Id);
    }

    /// <summary>
    /// Evaluates the grammar from the node with id <paramref name="startId"/>,
    /// which must be stored in <paramref name="graph"/>. See
    /// <see cref="Evaluate(TypedPropertyGraph, GraphNode)"/> for the verdict
    /// semantics; execution is deterministic and cycle-safe.
    /// </summary>
    public PathGrammarResult Evaluate(TypedPropertyGraph graph, string startId)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(startId);
        if (_steps.Length == 0)
        {
            throw new InvalidOperationException("A path grammar must declare at least one step before it can be evaluated.");
        }

        var start = graph.GetNode(startId);
        var validPaths = FindValidPaths(graph, start);
        var rejectedPaths = FindRejectedPaths(graph, start);
        return new PathGrammarResult(this, startId, validPaths, rejectedPaths);
    }

    /// <summary>
    /// Whether <paramref name="path"/> follows the grammar's permitted edge
    /// sequence: the same number of steps, each step traversing the grammar's
    /// step edge type in the grammar's step direction, with no step onto a
    /// repeated node. The node types along the path follow from the edge
    /// types, so a matching path is a semantically valid edge sequence in its
    /// entirety.
    /// </summary>
    public bool Matches(TraversalPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Steps.Count != _steps.Length || path.IsCycle)
        {
            return false;
        }

        for (var index = 0; index < _steps.Length; index++)
        {
            if (path.Steps[index].Edge.EdgeType != _steps[index].EdgeType
                || path.Steps[index].Direction != _steps[index].Direction)
            {
                return false;
            }
        }

        return true;
    }

    private PathGrammar Append(PathGrammarStep step)
    {
        var steps = new PathGrammarStep[_steps.Length + 1];
        Array.Copy(_steps, steps, _steps.Length);
        steps[^1] = step;
        return new PathGrammar(_registry, Name, steps);
    }

    private List<TraversalPath> FindValidPaths(TypedPropertyGraph graph, GraphNode start)
    {
        // The step-by-step walk: from the start, each grammar step fans out
        // over the stored edges of exactly that step's edge type read in the
        // step's direction, in the graph's insertion order.
        var frontier = new List<TraversalPath> { new(start, []) };
        for (var index = 0; index < _steps.Length; index++)
        {
            var step = _steps[index];
            var name = step.Direction == EdgeDirection.Forward ? step.EdgeType.ForwardName : step.EdgeType.InverseName;
            var next = new List<TraversalPath>();
            foreach (var path in frontier)
            {
                foreach (var edge in graph.GetEdges(path.End.Id, name).Where(edge => edge.EdgeType == step.EdgeType))
                {
                    var reachedId = edge.FromId == path.End.Id ? edge.ToId : edge.FromId;
                    if (!graph.TryGetNode(reachedId, out var reached))
                    {
                        // Unresolved edge: a path of nodes cannot step onto a
                        // missing node.
                        continue;
                    }

                    if (path.ContainsNode(reached))
                    {
                        // A step onto a repeated node closes a loop, and a
                        // loop is not a valid assurance chain — the route ends
                        // without a match.
                        continue;
                    }

                    next.Add(path.Append(new TraversalStep(reached, edge, name, edge.DirectionFrom(path.End.Id))));
                }
            }

            frontier = next;
        }

        return frontier;
    }

    private List<TraversalPath> FindRejectedPaths(TypedPropertyGraph graph, GraphNode start)
    {
        var rejected = new List<TraversalPath>();
        Extend(graph, new TraversalPath(start, []), rejected);
        return rejected;
    }

    private void Extend(TypedPropertyGraph graph, TraversalPath path, List<TraversalPath> rejected)
    {
        if (path.Steps.Count >= _steps.Length)
        {
            // A path longer than the grammar cannot match it: the candidate
            // space is bounded by the grammar's own length.
            return;
        }

        // Depth-first walk over every stored edge touching the path's end, in
        // the graph's insertion order. A route onto a repeated node ends
        // (simple paths only), and a route reaching the grammar's terminal
        // node type is classified: a match is reported through the valid
        // paths; everything else reaching the terminal through a different
        // sequence is rejected, so a disallowed conclusion is visible as
        // evidence rather than silently absent.
        foreach (var edge in graph.GetEdges(path.End.Id))
        {
            var reachedId = edge.FromId == path.End.Id ? edge.ToId : edge.FromId;
            if (!graph.TryGetNode(reachedId, out var reached) || path.ContainsNode(reached))
            {
                continue;
            }

            var extended = path.Append(
                new TraversalStep(
                    reached,
                    edge,
                    edge.FromId == path.End.Id ? edge.Name : edge.ReverseName,
                    edge.DirectionFrom(path.End.Id)));
            if (reached.Type.Name == EndType!.Name && !Matches(extended))
            {
                rejected.Add(extended);
            }

            Extend(graph, extended, rejected);
        }
    }
}
