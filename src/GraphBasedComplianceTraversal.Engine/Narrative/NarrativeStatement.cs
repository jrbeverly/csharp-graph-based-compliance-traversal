namespace GraphBasedComplianceTraversal.Engine.Narrative;

/// <summary>
/// One sentence of a compiled narrative and the step(s) and supporting facts
/// it maps to: <see cref="Text"/> is the deterministic template rendering of
/// the step(s) — compiled, not free-form, so the mapping is part of the
/// structure rather than an inference over prose — <see cref="Steps"/> are
/// the compiled steps the sentence was templated from, and
/// <see cref="Facts"/> are the supporting facts the sentence rests on, each
/// with its provenance. A verdict sentence — the established conclusion or
/// the explicit no-conclusion of a gap narrative — carries no steps and
/// only the subject facts its statement rests on. Every sentence of a
/// compiled narrative therefore maps to a step or a fact of the underlying
/// paths; no sentence maps to nothing.
/// </summary>
public sealed record NarrativeStatement(
    string Text,
    IReadOnlyList<NarrativeStep> Steps,
    IReadOnlyList<NarrativeFact> Facts);
