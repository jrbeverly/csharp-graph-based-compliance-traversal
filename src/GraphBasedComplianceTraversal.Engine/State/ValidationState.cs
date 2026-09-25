namespace GraphBasedComplianceTraversal.Engine.State;

/// <summary>
/// The single shared validation state model consumed by assertions, the
/// linter, and reports. <see cref="Unknown"/> is the default value of the
/// enumeration: any result that has not been explicitly resolved reads as
/// Unknown, so a check that never ran can never be mistaken for a pass.
/// </summary>
public enum ValidationState
{
    /// <summary>
    /// No observation exists for the property, so the check has not been
    /// resolved. The default state, by construction.
    /// </summary>
    Unknown = 0,

    /// <summary>The observed value matches the declared value.</summary>
    Pass,

    /// <summary>The observed value violates an invariant declared for the subject.</summary>
    Fail,

    /// <summary>The check does not apply to this subject.</summary>
    NotApplicable,

    /// <summary>The check is covered by an approved exception.</summary>
    Exception,

    /// <summary>The basis for the check (for example an exception or an acceptance) has expired.</summary>
    Expired,

    /// <summary>The last observation is too old to count as current evidence.</summary>
    Stale,

    /// <summary>Declared and observed values disagree, or two sources conflict.</summary>
    Conflicting,
}
