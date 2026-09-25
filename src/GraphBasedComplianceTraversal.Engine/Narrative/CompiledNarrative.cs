namespace GraphBasedComplianceTraversal.Engine.Narrative;

/// <summary>The verdict of a <see cref="CompiledNarrative"/>.</summary>
public enum NarrativeVerdict
{
    /// <summary>
    /// The narrative compiles validated paths into an established
    /// conclusion: every sentence maps to a step or fact of the underlying
    /// paths.
    /// </summary>
    Established,

    /// <summary>
    /// No validated path exists: the narrative states the gap explicitly and
    /// draws no conclusion. Nothing is inferred from the absence of a path.
    /// </summary>
    Gap,
}

/// <summary>
/// The structured explanation a narrative is rendered from: the compiled
/// result of running a validated path — or a no-valid-path verdict — through
/// <see cref="NarrativeCompiler"/>. The structure is immutable data: the
/// established <see cref="Steps"/> with their typed edges, the
/// <see cref="Statements"/> with the template rendering of each step and
/// the facts each sentence maps to, the distinct supporting
/// <see cref="Facts"/> with their provenance, and the explicit
/// <see cref="Verdict"/> — established paths, or the <see cref="Gaps"/>
/// stating where the required chain stops. A renderer —
/// <see cref="DefaultNarrativeRenderer"/> or any downstream
/// <see cref="INarrativeRenderer"/> — receives this structure and nothing
/// else: no graph, no store, no traversal engine. There is no surface
/// through which a renderer could add an edge, a fact, or a conclusion.
/// </summary>
public sealed class CompiledNarrative
{
    internal CompiledNarrative(
        string title,
        string subjectId,
        string? grammarName,
        int pathCount,
        NarrativeVerdict verdict,
        IReadOnlyList<NarrativeStep> steps,
        IReadOnlyList<NarrativeStatement> statements,
        IReadOnlyList<NarrativeGap> gaps)
    {
        Title = title;
        SubjectId = subjectId;
        GrammarName = grammarName;
        PathCount = pathCount;
        Verdict = verdict;
        Steps = steps;
        Statements = statements;
        Gaps = gaps;
        Facts = DistinctFacts(statements);
    }

    /// <summary>The compiled title line: the narrative's subject, as the compiler labeled it.</summary>
    public string Title { get; }

    /// <summary>The id of the node the narrative is about — the start of the compiled paths.</summary>
    public string SubjectId { get; }

    /// <summary>The name of the path grammar the narrative was compiled from, when one was evaluated.</summary>
    public string? GrammarName { get; }

    /// <summary>The number of validated paths the narrative compiles. Zero for a gap narrative.</summary>
    public int PathCount { get; }

    /// <summary>
    /// <see cref="NarrativeVerdict.Established"/> when the narrative compiles
    /// validated paths, <see cref="NarrativeVerdict.Gap"/> when no validated
    /// path exists and the narrative states the gap — never a fabricated
    /// conclusion.
    /// </summary>
    public NarrativeVerdict Verdict { get; }

    /// <summary>
    /// The compiled steps in first-seen order: every distinct step of the
    /// validated paths, each with its typed edge and supporting facts. Empty
    /// for a gap narrative.
    /// </summary>
    public IReadOnlyList<NarrativeStep> Steps { get; }

    /// <summary>
    /// The narrative's sentences in order — one per compiled step, then the
    /// verdict sentence. Each sentence maps to its step(s) and facts.
    /// </summary>
    public IReadOnlyList<NarrativeStatement> Statements { get; }

    /// <summary>
    /// The explicit gaps of a no-valid-path narrative, in traversal order.
    /// Empty when the verdict is established.
    /// </summary>
    public IReadOnlyList<NarrativeGap> Gaps { get; }

    /// <summary>
    /// The distinct supporting facts every statement maps to, in first-seen
    /// order — the complete traceability record of the narrative: each fact
    /// names what it states and the sources that established it.
    /// </summary>
    public IReadOnlyList<NarrativeFact> Facts { get; }

    private static List<NarrativeFact> DistinctFacts(IReadOnlyList<NarrativeStatement> statements)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var facts = new List<NarrativeFact>();
        foreach (var fact in statements.SelectMany(statement => statement.Facts))
        {
            if (seen.Add($"{fact.Kind}|{fact.NodeId}|{fact.Name}|{fact.TargetNodeId}|{fact.Value}"))
            {
                facts.Add(fact);
            }
        }

        return facts;
    }
}
