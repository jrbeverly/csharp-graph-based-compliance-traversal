using GraphBasedComplianceTraversal.Engine.State;

namespace GraphBasedComplianceTraversal.Engine.Views;

/// <summary>
/// The current observation state a view carries for one observation node its
/// paths reach: the observation's id, the state the observation store
/// surfaces (<see cref="ValidationState.Stale"/>
/// when the producing validator version is behind the assertion's current
/// one, else the recorded state), the state it was recorded with, and the
/// validator version that produced it. A view annotates the shared
/// observation nodes with this state — it never stores observations itself,
/// and it never aggregates the states into a blanket success: a recorded
/// failure stays a failure and an outdated observation stays visibly stale.
/// </summary>
public sealed record ViewObservation(
    string ObservationId,
    ValidationState State,
    ValidationState RecordedState,
    bool IsStale,
    string? ValidatorVersion);
