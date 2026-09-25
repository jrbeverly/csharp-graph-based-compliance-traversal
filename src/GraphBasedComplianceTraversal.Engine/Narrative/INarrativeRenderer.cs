namespace GraphBasedComplianceTraversal.Engine.Narrative;

/// <summary>
/// The rendering boundary of the narrative pipeline — the single downstream
/// entry point any renderer implements: the deterministic default renderer
/// (<see cref="DefaultNarrativeRenderer"/>, templated, no AI), or an
/// optional AI renderer attached at the same place. A renderer receives the
/// compiled narrative — the established steps, the statements and the facts
/// each sentence maps to, their provenance, and the explicit verdict — and
/// returns text. It receives no graph, no store, and no traversal engine,
/// and the compiled structure is immutable data, so a renderer has no
/// surface through which to add an edge, a fact, or a conclusion. The
/// contract of the boundary, which the deterministic default renderer
/// upholds and any implementation must preserve:
/// <list type="bullet">
/// <item><b>AI may verbalize an established path. AI may not invent a
/// missing edge.</b> A renderer may rephrase the statements it is given; it
/// must not introduce facts, edges, or conclusions absent from the compiled
/// input.</item>
/// <item>The verdict is part of the input: a
/// <see cref="NarrativeVerdict.Gap"/> narrative states that no valid path
/// exists, and a renderer must not render it as a conclusion.</item>
/// <item>Every statement keeps its mapping: rephrased text must remain
/// traceable to the same steps and facts the compiled statement maps to.
/// </item>
/// </list>
/// The default rendering (<see cref="NarrativeRendering.Render"/>) is
/// deterministic and AI-free; the demonstration never depends on an AI
/// renderer existing.
/// </summary>
public interface INarrativeRenderer
{
    /// <summary>Renders the compiled narrative into a human-readable text.</summary>
    string Render(CompiledNarrative narrative);
}
