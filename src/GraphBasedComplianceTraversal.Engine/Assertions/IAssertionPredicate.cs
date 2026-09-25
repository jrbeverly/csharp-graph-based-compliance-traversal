using GraphBasedComplianceTraversal.Engine.Graph;

namespace GraphBasedComplianceTraversal.Engine.Assertions;

/// <summary>
/// Decides, for one subject, whether the graph's facts satisfy an assertion's
/// invariant. The decision is an <see cref="AssertionEvaluation"/>: a
/// <see cref="State.ValidationState"/> from the shared state model, the
/// provenance of the facts consulted to decide it, and a short reason naming
/// the deciding facts. A predicate must be total and deterministic — the same
/// graph and subject always decide the same state. Facts that support the
/// invariant resolve to Pass and facts that violate it to Fail; a subject
/// with no supporting fact must resolve to Unknown — no predicate may pass a
/// subject by omission.
/// </summary>
public interface IAssertionPredicate
{
    /// <summary>Evaluates the predicate for one subject against the graph.</summary>
    AssertionEvaluation Evaluate(TypedPropertyGraph graph, GraphNode subject);
}
