using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Observations;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.State;
using GraphBasedComplianceTraversal.Engine.Traversal;

namespace GraphBasedComplianceTraversal.Engine.Narrative;

/// <summary>
/// The explainable narrative compiler: the deterministic compilation of a
/// validated traversal path — or a grammar result carrying an explicit
/// no-valid-path verdict — into a <see cref="CompiledNarrative"/>: a
/// structured explanation in which every sentence is templated from a step
/// of the underlying paths and maps to the supporting facts that establish
/// it, each with its provenance. The compiler is the last stage that sees
/// the graph: everything downstream — the deterministic default renderer or
/// an optional AI renderer — receives only the compiled structure, so a
/// renderer may verbalize an established path but has no surface through
/// which to invent a missing edge or a new fact.
/// </summary>
public static class NarrativeCompiler
{
    /// <summary>The node type name of observations, whose steps the narrative annotates with their recorded state.</summary>
    private const string ObservationTypeName = "Observation";

    /// <summary>
    /// Compiles one validated path into an established narrative: one
    /// sentence per step of the path, each mapped to its supporting facts,
    /// then the verdict sentence.
    /// </summary>
    public static CompiledNarrative Compile(
        TraversalPath path,
        IReadOnlyList<StoredObservation>? observationState = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Compile([path], observationState);
    }

    /// <summary>
    /// Compiles validated paths into an established narrative, in the order
    /// given. A step shared by several paths — the same stored edge between
    /// the same nodes — is compiled once and stated once. At least one path
    /// is required; compile a <see cref="PathGrammarResult"/> for the
    /// no-valid-path narrative.
    /// </summary>
    public static CompiledNarrative Compile(
        IReadOnlyList<TraversalPath> paths,
        IReadOnlyList<StoredObservation>? observationState = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            throw new ArgumentException(
                "At least one path is required to compile an established narrative; compile a PathGrammarResult for a no-valid-path narrative.",
                nameof(paths));
        }

        return CompileEstablished(paths, grammarName: null, observationState);
    }

    /// <summary>
    /// Compiles a grammar result: its valid paths into an established
    /// narrative when any exists, and its explicit
    /// <see cref="PathGrammarVerdict.NoValidPath"/> into a gap narrative —
    /// the required step no stored edge satisfied, the frontier node the
    /// edge is missing from, and no conclusion drawn — when none does. The
    /// graph is consulted only to locate the gap: the compilation itself
    /// reads nothing but the result's paths.
    /// </summary>
    public static CompiledNarrative Compile(
        PathGrammarResult result,
        TypedPropertyGraph graph,
        IReadOnlyList<StoredObservation>? observationState = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(graph);

        if (result.HasValidPath)
        {
            return CompileEstablished(result.ValidPaths, result.Grammar.Name, observationState);
        }

        return CompileGap(result, graph);
    }

    /// <summary>
    /// The verb phrase the narrative reads an edge queried under
    /// <paramref name="edgeName"/> as, between the two node labels — for
    /// example <c>satisfied-by</c> reads <c>is satisfied by</c>. Every name
    /// declared by the ontology slice resolves to a grammatical phrase;
    /// names outside the slice fall back to the name's words, with
    /// <c>is ... by</c> for an inverse read, so the phrase always follows
    /// the traversed direction and never misstates the step.
    /// </summary>
    public static string VerbPhrase(string edgeName, EdgeDirection direction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edgeName);
        return VerbPhrases.TryGetValue(edgeName, out var phrase)
            ? phrase
            : direction == EdgeDirection.Forward
                ? Words(edgeName)
                : $"is {Words(edgeName)} by";
    }

    // The verb phrase per edge name declared by the ontology slice — the
    // deterministic prose vocabulary of the narrative. Names are directional
    // (forward and inverse names are distinct entries), so a phrase never
    // depends on the direction the edge was traversed in.
    private static readonly Dictionary<string, string> VerbPhrases = new(StringComparer.Ordinal)
    {
        ["fulfills"] = "fulfills",
        ["fulfilled-by"] = "is fulfilled by",
        ["has-capability"] = "exposes the capability",
        ["belongs-to"] = "belongs to",
        ["implemented-by"] = "is implemented by",
        ["implements"] = "implements",
        ["produced-by"] = "is produced by",
        ["produces"] = "produces",
        ["built-from"] = "is built from",
        ["built-into"] = "is built into",
        ["committed-to"] = "is committed to",
        ["has-commits"] = "has commits",
        ["stored-in"] = "is stored in",
        ["contains"] = "contains",
        ["distributed-via"] = "is distributed via",
        ["distributes"] = "distributes",
        ["source"] = "builds from",
        ["builds"] = "builds",
        ["depends-on"] = "depends on",
        ["used-by"] = "is used by",
        ["serves"] = "serves",
        ["origin"] = "has origin",
        ["served-by"] = "is served by",
        ["supplies"] = "supplies",
        ["supplied-by"] = "is supplied by",
        ["affects"] = "affects",
        ["affected-by"] = "is affected by",
        ["results-in"] = "results in",
        ["results-from"] = "results from",
        ["mitigates"] = "mitigates",
        ["mitigated-by"] = "is mitigated by",
        ["informs"] = "informs",
        ["informed-by"] = "is informed by",
        ["supports"] = "supports",
        ["supported-by"] = "is supported by",
        ["derived-from"] = "is derived from",
        ["adopted-by"] = "is adopted by",
        ["applies-to"] = "applies to",
        ["in-scope-of"] = "is in scope of",
        ["validates"] = "validates",
        ["validated-by"] = "is validated by",
        ["observes"] = "observes",
        ["observed-by"] = "is observed by",
        ["requires"] = "requires",
        ["satisfies"] = "satisfies",
        ["satisfied-by"] = "is satisfied by",
        ["exception-to"] = "is an exception to",
        ["has-exception"] = "has exception",
    };

    private static CompiledNarrative CompileEstablished(
        IReadOnlyList<TraversalPath> paths,
        string? grammarName,
        IReadOnlyList<StoredObservation>? observationState)
    {
        var observations = IndexObservations(observationState);
        var steps = new List<NarrativeStep>();
        var statements = new List<NarrativeStatement>();
        var seenSteps = new HashSet<(string, string, string, string, EdgeDirection)>();

        foreach (var path in paths)
        {
            var from = path.Start;
            foreach (var traversalStep in path.Steps)
            {
                var to = traversalStep.Node;
                var signature = (
                    from.Id,
                    to.Id,
                    traversalStep.Name,
                    traversalStep.Edge.EdgeType.ForwardName,
                    traversalStep.Direction);
                if (seenSteps.Add(signature))
                {
                    var step = CompileStep(from, traversalStep, observations);
                    steps.Add(step);
                    statements.Add(CompileStepStatement(step, from, to, observations));
                }

                from = to;
            }
        }

        var subject = paths[0].Start;
        statements.Add(EstablishedVerdictStatement(subject, grammarName, paths.Count, steps.Count));
        return new CompiledNarrative(
            Title(grammarName, subject),
            subject.Id,
            grammarName,
            paths.Count,
            NarrativeVerdict.Established,
            steps,
            statements,
            []);
    }

    private static CompiledNarrative CompileGap(PathGrammarResult result, TypedPropertyGraph graph)
    {
        var start = graph.GetNode(result.StartId);
        var gaps = AnalyzeGaps(result, graph);
        var statements = new List<NarrativeStatement>();

        // One sentence per gap: the required step, the frontier node the
        // edge is missing from, and the node type the step had to reach —
        // the chain is stated to be broken where it broke, never silently
        // incomplete.
        foreach (var gap in gaps)
        {
            statements.Add(new NarrativeStatement(gap.Description, [], SubjectFacts(graph.GetNode(gap.FromNodeId))));
        }

        // Routes that reached the terminal type through a different
        // sequence are reported as rejected evidence — they exist, and they
        // do not satisfy the grammar.
        if (result.RejectedPaths.Count > 0)
        {
            statements.Add(new NarrativeStatement(
                $"Additionally, {result.RejectedPaths.Count} {Plural(result.RejectedPaths.Count, "route")} from {Label(start)} reached a {result.Grammar.EndType!.Name} node through a different edge sequence; none satisfies the grammar.",
                [],
                SubjectFacts(start)));
        }

        // The explicit no-conclusion: nothing is inferred from the absence
        // of a path.
        statements.Add(new NarrativeStatement(
            $"The {Words(result.Grammar.Name)} path from {Label(start)} does not exist: no conclusion is drawn, and nothing is inferred from the absence of a path.",
            [],
            SubjectFacts(start)));

        return new CompiledNarrative(
            Title(result.Grammar.Name, start),
            start.Id,
            result.Grammar.Name,
            0,
            NarrativeVerdict.Gap,
            [],
            statements,
            gaps);
    }

    private static NarrativeStep CompileStep(
        GraphNode from,
        TraversalStep traversalStep,
        Dictionary<string, StoredObservation> observations)
    {
        var to = traversalStep.Node;
        var edge = traversalStep.Edge;
        var facts = new List<NarrativeFact>
        {
            new(NarrativeFactKind.Edge, from.Id, traversalStep.Name, to.Id, null, edge.Provenance),
            NodeFact(from),
            NodeFact(to),
        };
        AddPropertyFact(facts, from, "name");
        AddPropertyFact(facts, to, "name");

        if (to.Type.Name == ObservationTypeName)
        {
            AddPropertyFact(facts, to, "result");
            AddPropertyFact(facts, to, "observed_at");
            AddPropertyFact(facts, to, "validator_version");

            // The store's current surface is a supporting fact only when it
            // differs from the recorded state — the sentence states it
            // exactly then, so a fact never rests a sentence on a source it
            // does not name.
            if (observations.TryGetValue(to.Id, out var stored) && stored.State != stored.RecordedState)
            {
                facts.Add(new NarrativeFact(NarrativeFactKind.State, to.Id, "state", null, StateName(stored.State), stored.Provenance));
            }
        }

        return new NarrativeStep(from.Id, to.Id, traversalStep.Name, edge.EdgeType.ForwardName, traversalStep.Direction, facts);
    }

    private static NarrativeStatement CompileStepStatement(
        NarrativeStep step,
        GraphNode from,
        GraphNode to,
        Dictionary<string, StoredObservation> observations)
    {
        var text = $"{Label(from)} {VerbPhrase(step.EdgeName, step.Direction)} {Label(to)}";

        // A step onto an observation states what was recorded: the result,
        // the timestamp, and the validator version — each a property fact
        // of the observation node with its own provenance.
        if (to.Type.Name == ObservationTypeName)
        {
            var clauses = new List<string>();
            if (ScalarText(to.Properties.GetValueOrDefault("result")) is { } result)
            {
                clauses.Add($"recorded {result}");
            }

            if (ScalarText(to.Properties.GetValueOrDefault("observed_at")) is { } observedAt)
            {
                clauses.Add($"at {observedAt}");
            }

            if (ScalarText(to.Properties.GetValueOrDefault("validator_version")) is { } validatorVersion)
            {
                clauses.Add($"by validator {validatorVersion}");
            }

            if (clauses.Count > 0)
            {
                text += ", " + string.Join(' ', clauses);
            }
        }

        text += ".";

        // The store's current surface is stated when it differs from the
        // recorded state — an outdated observation stays visibly outdated
        // instead of being smoothed into current evidence.
        if (observations.TryGetValue(to.Id, out var stored) && stored.State != stored.RecordedState)
        {
            text += $" The store currently surfaces {StateName(stored.State)} (recorded {StateName(stored.RecordedState)}).";
        }

        return new NarrativeStatement(text, [step], step.Facts);
    }

    private static NarrativeStatement EstablishedVerdictStatement(
        GraphNode subject,
        string? grammarName,
        int pathCount,
        int stepCount)
    {
        var pathPhrase = grammarName is null ? "chain" : $"{Words(grammarName)} path";
        return new NarrativeStatement(
            $"The {pathPhrase} from {Label(subject)} is established: {Plural(pathCount, "validated path")}, {Plural(stepCount, "distinct step")}, every hop a stored typed edge of the shared graph.",
            [],
            SubjectFacts(subject));
    }

    /// <summary>
    /// Replays the grammar's permitted steps from the result's start — the
    /// same walk <see cref="PathGrammar.Evaluate(TypedPropertyGraph, string)"/>
    /// performs, step by step — to find the first step at which the frontier
    /// goes empty, and reports one gap per frontier node the required edge is
    /// missing from. A no-valid-path result always reaches such a step.
    /// </summary>
    private static NarrativeGap[] AnalyzeGaps(PathGrammarResult result, TypedPropertyGraph graph)
    {
        var frontier = new List<TraversalPath> { new(graph.GetNode(result.StartId), []) };
        foreach (var step in result.Grammar.Steps)
        {
            var name = step.Direction == EdgeDirection.Forward ? step.EdgeType.ForwardName : step.EdgeType.InverseName;
            var next = new List<TraversalPath>();
            foreach (var path in frontier)
            {
                foreach (var edge in graph.GetEdges(path.End.Id, name).Where(edge => edge.EdgeType == step.EdgeType))
                {
                    var reachedId = edge.FromId == path.End.Id ? edge.ToId : edge.FromId;
                    if (!graph.TryGetNode(reachedId, out var reached) || path.ContainsNode(reached))
                    {
                        continue;
                    }

                    next.Add(path.Append(new TraversalStep(reached, edge, name, edge.DirectionFrom(path.End.Id))));
                }
            }

            if (next.Count == 0)
            {
                return frontier
                    .Select(path => new NarrativeGap(
                        step.Name,
                        path.End.Id,
                        step.ToType.Name,
                        $"The chain breaks at the required step '{step.Name}': {Label(path.End)} has no stored '{step.Name}' edge reaching any {step.ToType.Name} node."))
                    .ToArray();
            }

            frontier = next;
        }

        return [];
    }

    private static NarrativeFact NodeFact(GraphNode node) =>
        new(NarrativeFactKind.Node, node.Id, node.Type.Name, null, null, node.Provenance);

    private static void AddPropertyFact(List<NarrativeFact> facts, GraphNode node, string property)
    {
        if (ScalarText(node.Properties.GetValueOrDefault(property)) is { } value)
        {
            facts.Add(new NarrativeFact(NarrativeFactKind.Property, node.Id, property, null, value, node.GetPropertyProvenance(property)));
        }
    }

    /// <summary>The facts a subject's sentence rests on: the node's existence and its name, when it has one.</summary>
    private static List<NarrativeFact> SubjectFacts(GraphNode subject)
    {
        var facts = new List<NarrativeFact> { NodeFact(subject) };
        AddPropertyFact(facts, subject, "name");
        return facts;
    }

    private static Dictionary<string, StoredObservation> IndexObservations(IReadOnlyList<StoredObservation>? observations)
    {
        var index = new Dictionary<string, StoredObservation>(StringComparer.Ordinal);
        if (observations is not null)
        {
            foreach (var observation in observations)
            {
                if (observation.ObservationId is not null && !index.ContainsKey(observation.ObservationId))
                {
                    index[observation.ObservationId] = observation;
                }
            }
        }

        return index;
    }

    /// <summary>The node label the narrative reads a node as: its name property quoted, with its id, or the bare id.</summary>
    private static string Label(GraphNode node) =>
        ScalarText(node.Properties.GetValueOrDefault("name")) is { } name
            ? $"\"{name}\" ({node.Id})"
            : node.Id;

    private static string Title(string? grammarName, GraphNode subject) =>
        $"{(grammarName is null ? "Path narrative" : CapitalizeWords(Words(grammarName)))}: {Label(subject)}";

    private static string StateName(ValidationState state) => state.ToString().ToLowerInvariant();

    private static string Words(string name) => name.Replace('-', ' ');

    private static string CapitalizeWords(string words) =>
        string.Join(' ', words.Split(' ').Select(word => char.ToUpperInvariant(word[0]) + word[1..]));

    private static string Plural(int count, string singular) => count == 1 ? $"{count} {singular}" : $"{count} {singular}s";

    private static string? ScalarText(JsonNode? value) =>
        value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) ? text : null;
}
