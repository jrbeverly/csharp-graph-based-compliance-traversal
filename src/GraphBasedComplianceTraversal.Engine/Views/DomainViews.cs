using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Observations;
using GraphBasedComplianceTraversal.Engine.Traversal;

namespace GraphBasedComplianceTraversal.Engine.Views;

/// <summary>
/// The named domain views over the shared graph — architecture, security, and
/// compliance as projection functions over the same underlying nodes. Each
/// view is built from a typed traversal or a path grammar over the graph's
/// stored edges and references the graph's own node instances; none copies
/// facts into a view-owned store, so the domains cannot drift apart.
/// <list type="bullet">
/// <item><see cref="Architecture"/> — the release architecture: the service,
/// the distribution it depends on, the artifact distributed through it, the
/// build pipeline that produced it, and the repository that build draws
/// from.</item>
/// <item><see cref="Security"/> — why the artifact-tampering risk matters
/// and how it is controlled: through the business impact it results in, the
/// control adoption that impact informed, the implementation realizing the
/// adoption, the assertion validating it, and every observation the
/// assertion produced.</item>
/// <item><see cref="Compliance"/> — the NIS2 supply-chain requirement as a
/// traversal entry point into the organizational graph: the supply-chain
/// assurance grammar evaluated from the requirement through the adoption,
/// the implementation, and the assertion to the observations — restating
/// nothing — with the observation store's current state (recorded failures
/// and outdated-validator entries included) carried alongside.</item>
/// </list>
/// </summary>
public static class DomainViews
{
    /// <summary>The release-distribution service the architecture view is projected from.</summary>
    public const string ArchitectureSubjectId = "service:release-distribution";

    /// <summary>The artifact-tampering risk the security view is projected from.</summary>
    public const string SecuritySubjectId = "risk:artifact-tampering";

    /// <summary>The NIS2 supply-chain requirement the compliance view is projected from.</summary>
    public const string ComplianceSubjectId = "requirement:nis2-article-21-supply-chain";

    /// <summary>
    /// The architecture view: the release chain in one typed walk from the
    /// service that distributes the Customer Agent — through the
    /// distribution it depends on, the artifact distributed through it, and
    /// the build pipeline that produced the artifact, to the source
    /// repository that build draws from. The walk starts at the service
    /// because the service is the release architecture's entry point; every
    /// hop is a stored edge of the shared graph, so the view covers the
    /// service, repository, build, artifact, and distribution the planning
    /// input names without declaring any of them twice.
    /// </summary>
    public static GraphView Architecture(TypedPropertyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var subject = graph.GetNode(ArchitectureSubjectId);
        var paths = new GraphTraversal(graph, subject)
            .Step("depends-on")
            .Step("distributes")
            .Step("produced-by")
            .Step("source")
            .Execute();
        return new GraphView("architecture", subject, paths, []);
    }

    /// <summary>
    /// The security/risk view: the artifact-tampering risk through the
    /// business impact it results in, the control adoption that impact
    /// informed, the implementation realizing the adoption, the assertion
    /// validating it, and every observation the assertion produced — the
    /// same chain <see cref="CanonicalTraversals.RiskToControlAndObservations"/>
    /// declares, projected as a view over the shared nodes.
    /// </summary>
    public static GraphView Security(TypedPropertyGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var subject = graph.GetNode(SecuritySubjectId);
        var paths = CanonicalTraversals.RiskToControlAndObservations(graph).Execute();
        return new GraphView("security", subject, paths, []);
    }

    /// <summary>
    /// The compliance view: the NIS2 supply-chain requirement as an entry
    /// point into the organizational graph. The view is produced entirely by
    /// traversal over shared nodes — the supply-chain assurance grammar
    /// evaluated from the requirement, never a compliance-specific store of
    /// control descriptions — and it carries the observation store's current
    /// state for every observation the valid paths reach: a recorded failure
    /// stays a failure and an outdated observation stays stale, so the view
    /// reports the seeded <c>FAIL</c> and <c>STALE</c> signals rather than a
    /// blanket success. An observation node the store holds no entry for is
    /// simply not annotated; when no valid assurance path exists the view
    /// carries no paths and no observation state — never a fabricated
    /// satisfaction.
    /// </summary>
    public static GraphView Compliance(TypedPropertyGraph graph, ObservationStore store)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(store);

        var subject = graph.GetNode(ComplianceSubjectId);
        var result = CanonicalPathGrammars.SupplyChainAssurance(graph.Registry).Evaluate(graph, subject);
        var observations = new List<ViewObservation>();
        foreach (var path in result.ValidPaths)
        {
            // The assurance chain is requirement → adoption → implementation
            // → assertion → observation, so the node before the path's end
            // is the assertion the observation was produced by.
            var assertionId = path.Nodes[^2].Id;
            var entry = store.Timeline(assertionId).SingleOrDefault(stored => stored.ObservationId == path.End.Id);
            if (entry is not null)
            {
                observations.Add(new ViewObservation(
                    path.End.Id,
                    entry.State,
                    entry.RecordedState,
                    entry.IsStale,
                    entry.ValidatorVersion));
            }
        }

        return new GraphView("compliance", subject, result.ValidPaths, observations);
    }
}
