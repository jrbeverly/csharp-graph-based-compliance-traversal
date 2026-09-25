using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Observations;
using GraphBasedComplianceTraversal.Engine.Traversal;
using GraphBasedComplianceTraversal.Engine.Views;

namespace GraphBasedComplianceTraversal.Engine.Narrative;

/// <summary>
/// The demonstrated narratives of the shared world, compiled the way
/// <see cref="DomainViews"/> projects the views: entirely by traversal over
/// the graph's own nodes, never from narrative-owned copies. The supply-chain
/// compliance conclusion is the supply-chain assurance grammar evaluated
/// from the NIS2 requirement — the same derivation the compliance view
/// performs — compiled into a <see cref="CompiledNarrative"/> whose
/// observation steps carry the observation store's current state, the
/// recorded <c>FAIL</c> and the outdated-validator <c>STALE</c> surfaces
/// included.
/// </summary>
public static class DomainNarratives
{
    /// <summary>The assertion whose observations the supply-chain compliance narrative states.</summary>
    public const string SupplyChainAssertionId = "assertion:prod-artifact-provenance";

    /// <summary>
    /// Compiles the supply-chain compliance narrative: the NIS2 supply-chain
    /// requirement as the entry point, the supply-chain assurance grammar
    /// evaluated from it, and the observation store's current state for
    /// every observation the valid paths reach. When no valid assurance path
    /// exists the compiled narrative states the gap explicitly and draws no
    /// conclusion — never a fabricated satisfaction.
    /// </summary>
    public static CompiledNarrative SupplyChainCompliance(TypedPropertyGraph graph, ObservationStore store)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(store);

        var subject = graph.GetNode(DomainViews.ComplianceSubjectId);
        var result = CanonicalPathGrammars.SupplyChainAssurance(graph.Registry).Evaluate(graph, subject);
        return NarrativeCompiler.Compile(result, graph, store.Timeline(SupplyChainAssertionId));
    }
}
