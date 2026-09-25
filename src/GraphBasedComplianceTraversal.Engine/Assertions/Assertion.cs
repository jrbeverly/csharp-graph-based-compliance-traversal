using GraphBasedComplianceTraversal.Engine.Graph;

namespace GraphBasedComplianceTraversal.Engine.Assertions;

/// <summary>
/// A property the organization expects to remain true, made evaluable against
/// the graph: a stable id, a one-sentence statement of the invariant, a
/// subject selector deciding which graph nodes the property applies to, and a
/// predicate deciding, per subject, whether the graph's facts satisfy the
/// property. An assertion is not evidence — it only declares what should hold;
/// evaluating it produces <see cref="AssertionObservation"/> records, and
/// those observations are the evidence.
/// </summary>
public sealed class Assertion
{
    /// <summary>
    /// Creates an assertion. The id is the same stable identifier an
    /// <c>Assertion</c> record under <c>data/**</c> would carry (for example
    /// <c>assertion:prod-artifact-provenance</c>), so an evaluated assertion
    /// relates back to the graph node that declares it.
    /// </summary>
    public Assertion(string id, string statement, IAssertionSubjectSelector selector, IAssertionPredicate predicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(predicate);

        Id = id;
        Statement = statement;
        Selector = selector;
        Predicate = predicate;
    }

    /// <summary>The stable identifier of the assertion, for example <c>assertion:prod-artifact-provenance</c>.</summary>
    public string Id { get; }

    /// <summary>The invariant in one sentence, for example "Every production artifact has valid provenance."</summary>
    public string Statement { get; }

    /// <summary>The subject selector deciding which graph nodes the assertion applies to.</summary>
    public IAssertionSubjectSelector Selector { get; }

    /// <summary>The predicate deciding, per subject, whether the graph's facts satisfy the invariant.</summary>
    public IAssertionPredicate Predicate { get; }

    /// <summary>
    /// Evaluates the assertion against the graph, producing one observation
    /// per selected subject, in selector order. Evaluation is deterministic:
    /// every state is decided from the graph's facts alone, the order follows
    /// the selector, and <paramref name="evaluatedAt"/> is the timestamp every
    /// produced observation carries — the evaluator never reads the clock. A
    /// subject whose facts do not support the property yields
    /// <see cref="State.ValidationState.Unknown"/>, never an implied pass.
    /// </summary>
    public IReadOnlyList<AssertionObservation> Evaluate(TypedPropertyGraph graph, DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return Selector.SelectSubjects(graph)
            .Select(subject => Observe(subject, Predicate.Evaluate(graph, subject), evaluatedAt))
            .ToArray();
    }

    private AssertionObservation Observe(GraphNode subject, AssertionEvaluation evaluation, DateTimeOffset evaluatedAt) =>
        new(Id, subject.Id, evaluation.State, evaluatedAt, evaluation.Provenance, evaluation.Reason);
}
