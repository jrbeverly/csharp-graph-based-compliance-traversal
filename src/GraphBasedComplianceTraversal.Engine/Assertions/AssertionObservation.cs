using GraphBasedComplianceTraversal.Engine.Provenance;
using GraphBasedComplianceTraversal.Engine.State;

namespace GraphBasedComplianceTraversal.Engine.Assertions;

/// <summary>
/// The observation one assertion evaluation produces for one subject: which
/// assertion was evaluated, which subject it was evaluated for, the
/// <see cref="ValidationState"/> the subject's facts resolved to, the point
/// in time of the evaluation, the provenance of the facts the predicate
/// consulted, and a short reason naming the deciding facts. Observations are
/// the evidence side effect of validation — they exist because the assertion
/// was evaluated, never as a separately collected artifact.
/// </summary>
public sealed record AssertionObservation(
    string AssertionId,
    string SubjectId,
    ValidationState State,
    DateTimeOffset EvaluatedAt,
    FactProvenance Provenance,
    string Reason);
