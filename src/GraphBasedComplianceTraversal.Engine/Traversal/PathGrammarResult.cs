namespace GraphBasedComplianceTraversal.Engine.Traversal;

/// <summary>The verdict of a <see cref="PathGrammarResult"/>.</summary>
public enum PathGrammarVerdict
{
    /// <summary>At least one path followed the grammar's permitted edge sequence.</summary>
    ValidPath,

    /// <summary>
    /// No path from the start followed the grammar — an explicit no-valid-path
    /// verdict, never a fabricated conclusion.
    /// </summary>
    NoValidPath,
}

/// <summary>
/// The outcome of evaluating a <see cref="PathGrammar"/> from a start node:
/// the paths that followed the grammar's permitted edge sequence, the paths
/// from the same start that reached the grammar's terminal node type through
/// a different sequence, and the explicit verdict over them. Both lists are
/// ordered deterministically — valid paths in step-by-step fan-out order and
/// rejected paths in depth-first enumeration order over the graph's insertion
/// order — and a result with no valid paths carries
/// <see cref="PathGrammarVerdict.NoValidPath"/> rather than a fabricated path.
/// </summary>
public sealed class PathGrammarResult
{
    internal PathGrammarResult(
        PathGrammar grammar,
        string startId,
        IReadOnlyList<TraversalPath> validPaths,
        IReadOnlyList<TraversalPath> rejectedPaths)
    {
        Grammar = grammar;
        StartId = startId;
        ValidPaths = validPaths;
        RejectedPaths = rejectedPaths;
    }

    /// <summary>The grammar that was evaluated.</summary>
    public PathGrammar Grammar { get; }

    /// <summary>The id of the node the grammar was evaluated from.</summary>
    public string StartId { get; }

    /// <summary>
    /// The paths that followed the grammar's permitted edge sequence, in
    /// deterministic traversal order — one per valid route, so a requirement
    /// satisfiable by more than one valid path returns them all. Empty when no
    /// path matched, which <see cref="Verdict"/> reports explicitly.
    /// </summary>
    public IReadOnlyList<TraversalPath> ValidPaths { get; }

    /// <summary>
    /// The paths from the start that reached the grammar's terminal node type
    /// through a different edge sequence, in deterministic enumeration order.
    /// Reporting them keeps a disallowed conclusion visible as evidence — the
    /// grammar rejects it rather than swallowing it — and they never overlap
    /// <see cref="ValidPaths"/>.
    /// </summary>
    public IReadOnlyList<TraversalPath> RejectedPaths { get; }

    /// <summary>True when at least one path followed the grammar's permitted edge sequence.</summary>
    public bool HasValidPath => ValidPaths.Count > 0;

    /// <summary>
    /// The explicit verdict: <see cref="PathGrammarVerdict.ValidPath"/> when a
    /// valid path exists, <see cref="PathGrammarVerdict.NoValidPath"/>
    /// otherwise — whether no route reached the grammar's terminal node type
    /// at all, or every route that did was through a disallowed sequence.
    /// </summary>
    public PathGrammarVerdict Verdict => HasValidPath ? PathGrammarVerdict.ValidPath : PathGrammarVerdict.NoValidPath;
}
