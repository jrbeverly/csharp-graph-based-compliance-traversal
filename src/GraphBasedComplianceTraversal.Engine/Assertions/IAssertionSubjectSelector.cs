using GraphBasedComplianceTraversal.Engine.Graph;

namespace GraphBasedComplianceTraversal.Engine.Assertions;

/// <summary>
/// Selects the graph nodes an assertion applies to: a class/type of subjects
/// ("every production artifact") or an arbitrary graph query ("every
/// production resource"). The selected subjects decide the observations an
/// evaluation produces — one observation per subject — so a selector must
/// return each subject at most once. The built-in selectors order subjects by
/// graph insertion order, which keeps evaluation deterministic.
/// </summary>
public interface IAssertionSubjectSelector
{
    /// <summary>The graph nodes the assertion applies to, in evaluation order.</summary>
    IReadOnlyList<GraphNode> SelectSubjects(TypedPropertyGraph graph);
}
