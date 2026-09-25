using System.Text.Json.Nodes;

namespace GraphBasedComplianceTraversal.Engine.State;

/// <summary>
/// The resolution of a single property on a <see cref="FactSubject"/>: the
/// declared value (what the organization says should exist), the observed
/// value (what an external system measured), and the resulting
/// <see cref="ValidationState"/>. Both values are preserved even when they
/// disagree, so a <see cref="ValidationState.Conflicting"/> resolution never
/// hides which values conflicted.
/// </summary>
public readonly record struct ResolvedFact(
    string PropertyName,
    ValidationState State,
    JsonNode? DeclaredValue,
    JsonNode? ObservedValue)
{
    /// <summary>True when the property carries a declared value.</summary>
    public bool HasDeclaration => DeclaredValue is not null;

    /// <summary>True when the property carries an observed value.</summary>
    public bool HasObservation => ObservedValue is not null;
}
