namespace GraphBasedComplianceTraversal.Engine.Narrative;

/// <summary>
/// The deterministic default renderer: the AI-free
/// <see cref="INarrativeRenderer"/> implementation the demonstration runs,
/// assembling the document through <see cref="NarrativeRendering"/> — pure
/// templated assembly of the compiled structure, so the demonstration never
/// depends on a model. An optional AI renderer would implement the same
/// interface at the same boundary and receive the same compiled input;
/// <see cref="INarrativeRenderer"/> documents what it may and may not do
/// with it.
/// </summary>
public sealed class DefaultNarrativeRenderer : INarrativeRenderer
{
    /// <inheritdoc />
    public string Render(CompiledNarrative narrative) => NarrativeRendering.Render(narrative);
}
