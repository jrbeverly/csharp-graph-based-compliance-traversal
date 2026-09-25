using GraphBasedComplianceTraversal.Engine.Graph;

namespace GraphBasedComplianceTraversal.Engine.Assertions;

/// <summary>
/// The single authoritative assertion slice for this repository: the
/// assertions the fixtures imply, in evaluation order, each carrying a stable
/// id, a statement, a subject selector, and a predicate. Assertion ids are
/// unique — the id namespace of an <c>Assertion</c> node under
/// <c>data/**</c> — so an evaluated assertion can be looked up by the same id
/// the declared world uses. Evaluation is deterministic: assertions evaluate
/// in registration order and each produces one observation per selected
/// subject in selector order.
/// </summary>
public sealed class AssertionRegistry
{
    private readonly Dictionary<string, Assertion> _byId;

    /// <summary>Creates a registry from the given assertions.</summary>
    public AssertionRegistry(IReadOnlyList<Assertion> assertions)
    {
        ArgumentNullException.ThrowIfNull(assertions);
        if (assertions.Count == 0)
        {
            throw new ArgumentException("At least one assertion must be registered.", nameof(assertions));
        }

        Assertions = assertions;
        _byId = new Dictionary<string, Assertion>(StringComparer.Ordinal);
        foreach (var assertion in assertions)
        {
            ArgumentNullException.ThrowIfNull(assertion);
            if (!_byId.TryAdd(assertion.Id, assertion))
            {
                throw new ArgumentException($"Assertion id '{assertion.Id}' is registered more than once.", nameof(assertions));
            }
        }
    }

    /// <summary>The single authoritative assertion slice for this repository.</summary>
    public static AssertionRegistry Slice { get; } = AssertionDefinition.Create();

    /// <summary>The assertions, in evaluation order.</summary>
    public IReadOnlyList<Assertion> Assertions { get; }

    /// <summary>The assertion with the given id, or <c>null</c> when none carries it.</summary>
    public Assertion? Find(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _byId.TryGetValue(id, out var assertion) ? assertion : null;
    }

    /// <summary>The assertion with the given id, or throws when none carries it.</summary>
    public Assertion Require(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _byId.TryGetValue(id, out var assertion)
            ? assertion
            : throw new KeyNotFoundException($"No assertion with id '{id}' is in the registry.");
    }

    /// <summary>
    /// Evaluates every assertion against the graph, in registration order.
    /// Each assertion contributes one observation per selected subject, so the
    /// result is deterministic given a deterministic graph. The
    /// <paramref name="evaluatedAt"/> timestamp is shared by every produced
    /// observation.
    /// </summary>
    public IReadOnlyList<AssertionObservation> EvaluateAll(TypedPropertyGraph graph, DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return Assertions
            .SelectMany(assertion => assertion.Evaluate(graph, evaluatedAt))
            .ToArray();
    }
}
