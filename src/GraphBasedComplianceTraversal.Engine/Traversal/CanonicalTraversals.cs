using GraphBasedComplianceTraversal.Engine.Graph;

namespace GraphBasedComplianceTraversal.Engine.Traversal;

/// <summary>
/// The canonical cross-domain traversals the planning input names, each a
/// ready-built <see cref="GraphTraversal"/> query over the shared graph —
/// architecture, security, and risk as different paths through one graph
/// rather than independent representations. Each query names its steps
/// exactly as the ontology slice declares them: the infrastructure-to-business
/// chain reads the inverse names over the same stored edges the
/// business-to-infrastructure chain reads forward.
/// <list type="bullet">
/// <item><see cref="ResourceToPurpose"/> — why a resource exists:
/// <c>aws:s3:prod-release-artifacts</c> up through the service that uses it,
/// the capability the service implements, the product the capability belongs
/// to, to the business purpose the product fulfills.</item>
/// <item><see cref="PurposeToTechnicalSystems"/> — the reverse: from a
/// business purpose down to the technical systems that realize it, fanning
/// out over the service's dependencies at the last step.</item>
/// <item><see cref="CapabilityToRealizingTechnicalSystems"/> — from a
/// business capability to the technical systems that realize it.</item>
/// <item><see cref="ArtifactBuildAndSource"/> — an artifact to the build
/// pipeline that produced it, the source repository the pipeline builds
/// from, and the source commit in that repository.</item>
/// <item><see cref="ArtifactAttestedDerivation"/> — the same artifact along
/// the attested derivation chain: the build run the provenance attestation
/// binds it to, that run's source commit, and its repository. The
/// <c>produced-by</c> step's edge carries the attestation's provenance
/// records, so the path names the provenance information that exists for
/// the artifact.</item>
/// <item><see cref="ArtifactDistribution"/> — an artifact to where it is
/// distributed.</item>
/// <item><see cref="RiskToControlAndObservations"/> — why a risk matters and
/// how it is controlled: the business impact it results in, the control
/// adoption that impact informed, the adoption's implementation, the
/// assertion validating it, and every observation that assertion produced.</item>
/// </list>
/// The queries are the deliverable's canonical shape; rendering them as
/// explained paths is <see cref="ExplainedPath"/>'s job, and
/// <see cref="GraphTraversal.ExecuteWithGaps"/> reports the unresolved
/// references any of them hit — the reverse chains pass product capabilities
/// without records, whose <c>has-capability</c> edges surface as gaps rather
/// than silent stops.
/// </summary>
public static class CanonicalTraversals
{
    /// <summary>
    /// Infrastructure to business: the bucket, the service that uses it, the
    /// capability the service implements, the product the capability belongs
    /// to, and the business purpose the product fulfills.
    /// </summary>
    public static GraphTraversal ResourceToPurpose(TypedPropertyGraph graph) =>
        new GraphTraversal(graph, "aws:s3:prod-release-artifacts")
            .Step("used-by")
            .Step("implements")
            .Step("belongs-to")
            .Step("fulfills");

    /// <summary>
    /// Business to infrastructure: the purpose's product, its capabilities
    /// (two of which are unresolved references the report surfaces as gaps),
    /// the service realizing the resolved capability, and the technical
    /// systems that service depends on.
    /// </summary>
    public static GraphTraversal PurposeToTechnicalSystems(TypedPropertyGraph graph) =>
        new GraphTraversal(graph, "business-purpose:secure-endpoint-management")
            .Step("fulfilled-by")
            .Step("has-capability")
            .Step("implemented-by")
            .Step("depends-on");

    /// <summary>
    /// A business capability to the technical systems that realize it: every
    /// service the capability is implemented by.
    /// </summary>
    public static GraphTraversal CapabilityToRealizingTechnicalSystems(TypedPropertyGraph graph) =>
        new GraphTraversal(graph, "capability:software-update-delivery")
            .Step("implemented-by");

    /// <summary>
    /// An artifact to its declared build chain: the build pipeline that
    /// produced it, the source repository that pipeline builds from, and the
    /// source commit committed to that repository.
    /// </summary>
    public static GraphTraversal ArtifactBuildAndSource(TypedPropertyGraph graph) =>
        new GraphTraversal(graph, "artifact:customer-agent-2.8.4")
            .Step("produced-by")
            .Step("source")
            .Step("has-commits");

    /// <summary>
    /// An artifact along the attested derivation chain: the build run the
    /// provenance attestation binds the artifact to, the source commit that
    /// run was built from, and the repository that commit was committed to.
    /// </summary>
    public static GraphTraversal ArtifactAttestedDerivation(TypedPropertyGraph graph) =>
        new GraphTraversal(graph, "artifact:customer-agent-2.8.4")
            .Step("produced-by")
            .Step("built-from")
            .Step("committed-to");

    /// <summary>An artifact to the distribution it reaches customers through.</summary>
    public static GraphTraversal ArtifactDistribution(TypedPropertyGraph graph) =>
        new GraphTraversal(graph, "artifact:customer-agent-2.8.4")
            .Step("distributed-via");

    /// <summary>
    /// A risk to the control machinery addressing it, by way of its business
    /// impact: the impact the risk results in, the control adoption that
    /// impact informed, the implementation realizing that adoption, the
    /// assertion validating the implementation, and every observation the
    /// assertion produced.
    /// </summary>
    public static GraphTraversal RiskToControlAndObservations(TypedPropertyGraph graph) =>
        new GraphTraversal(graph, "risk:artifact-tampering")
            .Step("results-in")
            .Step("informs")
            .Step("implemented-by")
            .Step("validated-by")
            .Step("produces");
}
