using GraphBasedComplianceTraversal.Engine.Provenance;
using GraphBasedComplianceTraversal.Engine.State;

namespace GraphBasedComplianceTraversal.Engine.Observations;

/// <summary>
/// One entry of the repository's recorded observation history — a member of
/// an <c>observations</c> list under <c>data/**</c>, for example
/// <c>data/security/observations/provenance-observations.yaml</c>: which
/// assertion the entry observed, the subject artifact it observed, the
/// recorded <c>PASS</c>/<c>FAIL</c> result, the point in time it was observed,
/// the version of the validator that produced it, the free-text details, and
/// the declaration provenance naming the document the entry came from. The
/// validator version is what makes the entry's staleness decidable — an
/// observation produced by a validator version older than the assertion's
/// current validator is surfaced stale by the <see cref="ObservationStore"/>,
/// never discarded.
/// </summary>
public sealed record HistoricalObservation(
    string Id,
    string AssertionId,
    string SubjectId,
    ValidationState Result,
    DateTimeOffset ObservedAt,
    string ValidatorVersion,
    string Details,
    FactProvenance Provenance,
    string? SubjectDigest,
    string? RelatedIncident);
