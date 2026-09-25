using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Provenance;
using GraphBasedComplianceTraversal.Engine.State;

namespace GraphBasedComplianceTraversal.Engine.Reconciliation;

/// <summary>
/// The declared-versus-observed reconciliation of one property of one subject:
/// the value the organization's record declares and the value an external
/// system observes, each with the provenance of the records that stated it,
/// and the verdict derived from comparing them:
/// <list type="bullet">
/// <item><see cref="ValidationState.Pass"/> — the values deep-equal: the
/// observation corroborates the declaration, and the fact is queryable as
/// agreeing across two independent sources;</item>
/// <item><see cref="ValidationState.Conflicting"/> — the values disagree: both
/// sides are retained, never silently reconciled to one value. Which source
/// to consult is recorded (<see cref="ClaimType"/>,
/// <see cref="Precedence"/>, <see cref="MostAuthoritative"/>), not decided —
/// resolution is a human concern the model only represents.</item>
/// </list>
/// A reconciliation exists only for a property both sides state; a property
/// only one side states is attached to the graph as that side's fact, with no
/// reconciliation verdict. Records are created by
/// <see cref="DeclaredObservedReconciler"/> and stored on the typed property
/// graph, so agreement and disagreement are both explicit graph information.
/// </summary>
public sealed record FactReconciliation
{
    /// <summary>
    /// Creates a reconciliation record. The state must be
    /// <see cref="ValidationState.Pass"/> (corroborated) or
    /// <see cref="ValidationState.Conflicting"/> — a record never exists for
    /// the states a missing side would produce. Both sides must carry a value
    /// and the provenance naming where each value was recorded.
    /// </summary>
    public FactReconciliation(
        string subjectId,
        string propertyName,
        ValidationState state,
        JsonNode declaredValue,
        FactProvenance declaredProvenance,
        JsonNode observedValue,
        FactProvenance observedProvenance,
        ClaimType claimType,
        SourcePrecedence precedence,
        ProvenanceRecord? mostAuthoritative)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);
        if (state is not (ValidationState.Pass or ValidationState.Conflicting))
        {
            throw new ArgumentOutOfRangeException(
                nameof(state),
                state,
                "A reconciliation record is only recorded when both sides carry a value, so its state is Pass (corroborated) or Conflicting.");
        }

        ArgumentNullException.ThrowIfNull(declaredValue);
        ArgumentNullException.ThrowIfNull(observedValue);
        ArgumentNullException.ThrowIfNull(declaredProvenance);
        ArgumentNullException.ThrowIfNull(observedProvenance);
        if (!Enum.IsDefined(claimType))
        {
            throw new ArgumentOutOfRangeException(nameof(claimType), claimType, "Not a declared ClaimType member.");
        }

        if (!Enum.IsDefined(precedence))
        {
            throw new ArgumentOutOfRangeException(nameof(precedence), precedence, "Not a declared SourcePrecedence member.");
        }

        SubjectId = subjectId;
        PropertyName = propertyName;
        State = state;
        DeclaredValue = declaredValue;
        DeclaredProvenance = declaredProvenance;
        ObservedValue = observedValue;
        ObservedProvenance = observedProvenance;
        ClaimType = claimType;
        Precedence = precedence;
        MostAuthoritative = mostAuthoritative;
    }

    /// <summary>The stable id of the subject the property belongs to, for example <c>aws:s3:prod-release-artifacts</c>.</summary>
    public string SubjectId { get; init; }

    /// <summary>The name of the reconciled property.</summary>
    public string PropertyName { get; init; }

    /// <summary>
    /// <see cref="ValidationState.Pass"/> when the observed value corroborates
    /// the declared one, <see cref="ValidationState.Conflicting"/> when they
    /// disagree. No other state is ever recorded.
    /// </summary>
    public ValidationState State { get; init; }

    /// <summary>The value the organization's record declares.</summary>
    public JsonNode DeclaredValue { get; init; }

    /// <summary>The provenance of the declared value: the records that stated it.</summary>
    public FactProvenance DeclaredProvenance { get; init; }

    /// <summary>The value the external system observes.</summary>
    public JsonNode ObservedValue { get; init; }

    /// <summary>The provenance of the observed value: the records that stated it.</summary>
    public FactProvenance ObservedProvenance { get; init; }

    /// <summary>The kind of claim the property makes, which selects the authority table the precedence is read from.</summary>
    public ClaimType ClaimType { get; init; }

    /// <summary>
    /// The authority-table outcome of comparing the two sides for
    /// <see cref="ClaimType"/>: <see cref="SourcePrecedence.LeftWins"/> when
    /// the declared side is the source to consult,
    /// <see cref="SourcePrecedence.RightWins"/> when the observed side is.
    /// The verdict is recorded, never applied — no value is overwritten.
    /// </summary>
    public SourcePrecedence Precedence { get; init; }

    /// <summary>
    /// The provenance record with the highest declared authority for
    /// <see cref="ClaimType"/> across both sides — the source the table says
    /// to consult — or <c>null</c> when no record's source is declared
    /// authoritative for the claim type.
    /// </summary>
    public ProvenanceRecord? MostAuthoritative { get; init; }

    /// <summary>True when the observed value corroborates the declared one.</summary>
    public bool IsCorroborated => State == ValidationState.Pass;

    /// <summary>True when the declared and observed values disagree.</summary>
    public bool IsConflicting => State == ValidationState.Conflicting;
}
