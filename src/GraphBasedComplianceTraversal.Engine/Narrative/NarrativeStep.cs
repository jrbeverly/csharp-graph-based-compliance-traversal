using GraphBasedComplianceTraversal.Engine.Ontology;

namespace GraphBasedComplianceTraversal.Engine.Narrative;

/// <summary>
/// One step of a compiled narrative: a step of a validated traversal path,
/// compiled into the typed edge it traversed and the ordered supporting
/// facts that establish it — the edge fact itself, the two nodes' existence,
/// the name properties the sentence reads, and, for a step onto an
/// observation, the recorded result, timestamp, validator version, and the
/// observation store's current surface — each with its provenance. A step
/// shared by several validated paths is compiled once: it is the same
/// stored edge between the same nodes, so it is the same established fact
/// stated once, and the sentence compiled from it maps to that step as it
/// appears in every path that traverses it.
/// </summary>
public sealed record NarrativeStep(
    string FromNodeId,
    string ToNodeId,
    string EdgeName,
    string EdgeTypeName,
    EdgeDirection Direction,
    IReadOnlyList<NarrativeFact> Facts);
