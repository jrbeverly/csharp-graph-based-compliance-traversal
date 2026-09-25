using GraphBasedComplianceTraversal.Engine.Provenance;
using GraphBasedComplianceTraversal.Engine.State;

namespace GraphBasedComplianceTraversal.Engine.Assertions;

/// <summary>
/// The outcome of applying an assertion's predicate to one subject: the
/// <see cref="ValidationState"/> the subject's facts resolved to, the
/// provenance of the facts the predicate consulted to decide it, and a short
/// reason naming the deciding facts. The provenance is the evaluation's
/// evidence trail — a state is never reported without saying which facts
/// produced it. For an <see cref="ValidationState.Unknown"/> decision the
/// provenance names the subject's fact set that was consulted and found to
/// carry no supporting fact, so even an unchecked property answers where the
/// absence was observed.
/// </summary>
public readonly record struct AssertionEvaluation(
    ValidationState State,
    FactProvenance Provenance,
    string Reason);
