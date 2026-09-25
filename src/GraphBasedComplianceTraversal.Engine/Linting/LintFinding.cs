using GraphBasedComplianceTraversal.Engine.State;

namespace GraphBasedComplianceTraversal.Engine.Linting;

/// <summary>
/// One finding of the organizational linter: the graph subject the finding is
/// about, the invariant the subject failed, and the resulting state from the
/// shared <see cref="ValidationState"/> model, plus a reason naming what was
/// expected and what is actually missing. Findings are produced by
/// <see cref="OrganizationalLinter"/>; a check that passes produces no
/// finding, and a subject whose supporting facts are merely absent yields an
/// <see cref="ValidationState.Unknown"/> finding — absence is never read as
/// success.
/// </summary>
public sealed record LintFinding
{
    /// <summary>
    /// Creates a finding. The subject names the graph node (or referenced id)
    /// the finding is about; the invariant names the failed check; the state
    /// is the resulting state from the shared validation state model, which is
    /// never <see cref="ValidationState.Pass"/> — a passing check produces no
    /// finding at all.
    /// </summary>
    public LintFinding(string subjectId, string invariant, ValidationState state, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(invariant);
        if (state == ValidationState.Pass)
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "A finding is only produced when a check does not pass.");
        }

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Not a declared ValidationState member.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        SubjectId = subjectId;
        Invariant = invariant;
        State = state;
        Reason = reason;
    }

    /// <summary>The stable id of the subject the finding is about.</summary>
    public string SubjectId { get; init; }

    /// <summary>The invariant the subject failed, from the linter's fixed check list.</summary>
    public string Invariant { get; init; }

    /// <summary>The resulting state from the shared validation state model.</summary>
    public ValidationState State { get; init; }

    /// <summary>What the invariant expected and what is missing or unhealthy.</summary>
    public string Reason { get; init; }
}
