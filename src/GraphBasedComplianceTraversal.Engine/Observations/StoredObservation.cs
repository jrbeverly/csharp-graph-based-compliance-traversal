using GraphBasedComplianceTraversal.Engine.Provenance;
using GraphBasedComplianceTraversal.Engine.State;

namespace GraphBasedComplianceTraversal.Engine.Observations;

/// <summary>
/// One entry of an <see cref="ObservationStore"/> timeline: an observation of
/// one assertion for one subject, either ingested from the repository's
/// recorded history — it then carries its history <see cref="ObservationId"/>
/// and the <see cref="ValidatorVersion"/> that produced it — or freshly
/// produced by evaluation, in which case <see cref="ObservationId"/> and
/// <see cref="ValidatorVersion"/> are <c>null</c> and the evaluation's
/// provenance and reason are kept. <see cref="RecordedState"/> preserves the
/// state the observation was recorded with — a failure stays a failure —
/// while <see cref="State"/> is the state the store surfaces:
/// <see cref="ValidationState.Stale"/> when the observation was produced by a
/// validator version older than the assertion's current validator version.
/// Outdated observations therefore stay queryable and visibly outdated
/// instead of being smoothed into current evidence.
/// </summary>
public sealed record StoredObservation(
    string AssertionId,
    string SubjectId,
    ValidationState RecordedState,
    DateTimeOffset ObservedAt,
    FactProvenance Provenance,
    string Reason,
    bool IsStale,
    string? ObservationId = null,
    string? ValidatorVersion = null,
    string? SubjectDigest = null,
    string? RelatedIncident = null)
{
    /// <summary>
    /// The state the store surfaces: <see cref="ValidationState.Stale"/> when
    /// the observation is stale, else the recorded state.
    /// </summary>
    public ValidationState State => IsStale ? ValidationState.Stale : RecordedState;
}
