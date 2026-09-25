namespace GraphBasedComplianceTraversal.Engine.Narrative;

/// <summary>
/// The explicit gap a no-valid-path result compiles to: the required grammar
/// step no stored edge satisfied, the frontier node the edge is missing
/// from, the node type the step had to reach, and the deterministic
/// templated <see cref="Description"/> of the break. A gap is reported, never
/// smoothed over: the narrative states where the required chain stops and
/// why, and draws no conclusion from the absence of a path.
/// </summary>
public sealed record NarrativeGap(
    string RequiredStepName,
    string FromNodeId,
    string RequiredNodeType,
    string Description);
