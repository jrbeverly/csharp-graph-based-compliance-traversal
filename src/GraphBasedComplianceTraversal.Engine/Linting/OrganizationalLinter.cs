using System.Globalization;
using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Observations;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.State;

namespace GraphBasedComplianceTraversal.Engine.Linting;

/// <summary>
/// The organizational linter: walks the reconciled graph and the observation
/// store and reports structural gaps and unhealthy state as an ordered list of
/// <see cref="LintFinding"/> records, the way a compiler reports type errors.
/// <list type="bullet">
/// <item><b>Structural checks</b> — a production resource with no owner, a
/// risk with no treatment, a control adoption with no implementation, an
/// implementation with no validating assertion, and an artifact with no
/// traversable path to a source repository.</item>
/// <item><b>Graph-health checks</b> — unresolved references (typed edges to
/// missing records and references through fields the ontology does not model),
/// expiring or expired exceptions, stale facts (the supplier risk
/// assessment), missing mappings (a requirement's absent supplier link), the
/// recorded observation history's failures and outdated-validator entries,
/// and declared-versus-observed <see cref="ValidationState.Conflicting"/>
/// facts.</item>
/// </list>
/// <see cref="Lint"/> is deterministic: checks run in a fixed order, subjects
/// are visited in graph insertion order and store timeline order, and the
/// reference timestamp is a parameter — the linter never reads the clock.
/// Missing information is never read as success: a subject whose supporting
/// facts are merely absent yields a finding (or
/// <see cref="ValidationState.Unknown"/>), never a silent pass, and a check
/// that passes produces no finding at all. See <c>docs/linter.md</c>.
/// </summary>
public static class OrganizationalLinter
{
    private const string ProductionResourceOwnerInvariant = "production-resource-owner";
    private const string RiskTreatmentInvariant = "risk-treatment";
    private const string AdoptionImplementationInvariant = "adoption-implementation";
    private const string ImplementationAssertionInvariant = "implementation-assertion";
    private const string ArtifactSourcePathInvariant = "artifact-source-path";
    private const string UnresolvedReferenceInvariant = "unresolved-reference";
    private const string UnmodeledReferenceInvariant = "unmodeled-reference";
    private const string ExceptionExpiryInvariant = "exception-expiry";
    private const string SupplierAssessmentFreshnessInvariant = "supplier-assessment-freshness";
    private const string RequirementSupplierMappingInvariant = "requirement-supplier-mapping";
    private const string DeclaredObservedReconciliationInvariant = "declared-observed-reconciliation";
    private const string ObservationResultInvariant = "observation-result";
    private const string ObservationValidatorStalenessInvariant = "observation-validator-staleness";

    private const string ProductionClassification = "production";
    private const string MitigatedByEdgeName = "mitigated-by";
    private const string ImplementedByEdgeName = "implemented-by";
    private const string ValidatedByEdgeName = "validated-by";
    private const string ProducedByEdgeName = "produced-by";

    /// <summary>An exception whose expiry falls within this window is flagged as expiring.</summary>
    private static readonly TimeSpan ExceptionWarningWindow = TimeSpan.FromDays(30);

    /// <summary>A supplier risk assessment older than this is flagged stale.</summary>
    private static readonly TimeSpan SupplierAssessmentMaxAge = TimeSpan.FromDays(60);

    /// <summary>
    /// Lints the given world: the reconciled graph (declared facts, observed
    /// facts, and reconciliation verdicts) and the observation store holding
    /// the recorded history. Returns the findings in a deterministic order:
    /// structural checks first (in the order they are listed above), then the
    /// graph-health checks, each visiting subjects in the graph's insertion
    /// order and the store's timeline order. The reference timestamp decides
    /// every age-based check; the same inputs always produce equal findings.
    /// </summary>
    public static IReadOnlyList<LintFinding> Lint(
        TypedPropertyGraph graph,
        ObservationStore store,
        DateTimeOffset referenceTimestamp)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(store);

        var referenceDate = referenceTimestamp.UtcDateTime.Date;
        var findings = new List<LintFinding>();

        LintProductionResourceOwners(graph, findings);
        LintRiskTreatments(graph, findings);
        LintAdoptionImplementations(graph, findings);
        LintImplementationAssertions(graph, findings);
        LintArtifactSourcePaths(graph, findings);

        LintUnresolvedEdges(graph, findings);
        LintUnmodeledReferences(graph, findings);
        LintExceptionExpiry(graph, findings, referenceDate);
        LintSupplierAssessmentFreshness(graph, findings, referenceDate);
        LintRequirementSupplierMappings(graph, findings);
        LintReconciliationConflicts(graph, findings);
        LintObservationResults(graph, store, findings);
        LintObservationValidatorStaleness(graph, store, findings);

        return findings;
    }

    /// <summary>
    /// Every production-classified cloud resource must declare an owner. A
    /// non-empty <c>owner</c>/<c>owned_by</c> property passes; one declared
    /// but empty fails; no owner fact at all is a finding — a production
    /// resource with no recorded owner is structurally invalid, not merely
    /// undocumented.
    /// </summary>
    private static void LintProductionResourceOwners(TypedPropertyGraph graph, List<LintFinding> findings)
    {
        foreach (var type in graph.Registry.NodeTypes.Where(type => type.Category == "Cloud"))
        {
            foreach (var node in graph.GetNodes(type))
            {
                if (!IsClassifiedProduction(node))
                {
                    continue;
                }

                if (OwnerText(node) is not { } owner)
                {
                    findings.Add(new LintFinding(
                        node.Id,
                        ProductionResourceOwnerInvariant,
                        ValidationState.Unknown,
                        "declares no owner fact, and a production resource must have an owner"));
                }
                else if (string.IsNullOrWhiteSpace(owner))
                {
                    findings.Add(new LintFinding(
                        node.Id,
                        ProductionResourceOwnerInvariant,
                        ValidationState.Fail,
                        "declares an empty owner"));
                }
            }
        }
    }

    /// <summary>
    /// Every risk must have a treatment — a mitigating control adoption. A
    /// resolved treatment edge passes; an edge naming an adoption with no
    /// record fails; no treatment fact at all is a finding.
    /// </summary>
    private static void LintRiskTreatments(TypedPropertyGraph graph, List<LintFinding> findings)
    {
        foreach (var node in graph.GetNodes(NodeTypeOf(graph, "Risk")))
        {
            LintRequiredTreatment(
                graph,
                node,
                MitigatedByEdgeName,
                RiskTreatmentInvariant,
                "treatment",
                "no treatment is declared: no mitigating control adoption is recorded for it",
                findings);
        }
    }

    /// <summary>
    /// Every control adoption must have an implementation. A resolved
    /// <c>implemented-by</c> edge passes; an edge naming an implementation
    /// with no record fails; no implementation fact at all is a finding.
    /// </summary>
    private static void LintAdoptionImplementations(TypedPropertyGraph graph, List<LintFinding> findings)
    {
        foreach (var node in graph.GetNodes(NodeTypeOf(graph, "ControlAdoption")))
        {
            LintRequiredTreatment(
                graph,
                node,
                ImplementedByEdgeName,
                AdoptionImplementationInvariant,
                "implementation",
                "no implementation is declared for the adopted control",
                findings);
        }
    }

    /// <summary>
    /// Every control implementation must be validated by an assertion. A
    /// resolved <c>validated-by</c> edge passes; an edge naming an assertion
    /// with no record fails; no validating assertion at all is a finding.
    /// </summary>
    private static void LintImplementationAssertions(TypedPropertyGraph graph, List<LintFinding> findings)
    {
        foreach (var node in graph.GetNodes(NodeTypeOf(graph, "ControlImplementation")))
        {
            LintRequiredTreatment(
                graph,
                node,
                ValidatedByEdgeName,
                ImplementationAssertionInvariant,
                "validating assertion",
                "no validating assertion is declared for the implementation",
                findings);
        }
    }

    /// <summary>
    /// The shared shape of the three "must have a supporting relationship"
    /// structural checks: no edges under the required name is a finding, an
    /// edge whose target record is missing fails, and a resolved edge passes
    /// silently (a passing check produces no finding).
    /// </summary>
    private static void LintRequiredTreatment(
        TypedPropertyGraph graph,
        GraphNode node,
        string edgeName,
        string invariant,
        string treatmentNoun,
        string missingReason,
        List<LintFinding> findings)
    {
        var treatments = graph.GetEdges(node.Id, edgeName);
        if (treatments.Count == 0)
        {
            findings.Add(new LintFinding(node.Id, invariant, ValidationState.Unknown, missingReason));
            return;
        }

        var missingTargets = treatments
            .SelectMany(edge => edge.MissingEndpointIds)
            .Where(missingId => missingId != node.Id)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (missingTargets.Length > 0)
        {
            findings.Add(new LintFinding(
                node.Id,
                invariant,
                ValidationState.Fail,
                $"the declared {treatmentNoun} '{string.Join("', '", missingTargets)}' has no record"));
        }
    }

    /// <summary>
    /// Every artifact must have a traversable path to a source repository. An
    /// artifact with no derivation fact (<c>produced-by</c>) at all is a
    /// finding — its source is unknown; an artifact whose derivation facts do
    /// not reach a source repository through any typed edge chain fails.
    /// </summary>
    private static void LintArtifactSourcePaths(TypedPropertyGraph graph, List<LintFinding> findings)
    {
        foreach (var node in graph.GetNodes(NodeTypeOf(graph, "Artifact")))
        {
            var derivations = graph.GetEdges(node.Id, ProducedByEdgeName);
            if (derivations.Count == 0)
            {
                findings.Add(new LintFinding(
                    node.Id,
                    ArtifactSourcePathInvariant,
                    ValidationState.Unknown,
                    "declares no derivation fact: no produced-by relationship is recorded, so its source is unknown"));
            }
            else if (!Reaches(graph, node, candidate => candidate.Type.Name == "SourceRepository"))
            {
                findings.Add(new LintFinding(
                    node.Id,
                    ArtifactSourcePathInvariant,
                    ValidationState.Fail,
                    "no traversable path from its derivation facts reaches a source repository"));
            }
        }
    }

    /// <summary>
    /// Every typed edge whose endpoint has no stored node is an unresolved
    /// reference: the relationship is declared, its target record is not. One
    /// finding per missing endpoint, naming the missing id.
    /// </summary>
    private static void LintUnresolvedEdges(TypedPropertyGraph graph, List<LintFinding> findings)
    {
        foreach (var edge in graph.GetUnresolvedEdges())
        {
            foreach (var missingId in edge.MissingEndpointIds)
            {
                var referencer = edge.FromId == missingId ? edge.ToId : edge.FromId;
                findings.Add(new LintFinding(
                    missingId,
                    UnresolvedReferenceInvariant,
                    ValidationState.Unknown,
                    $"referenced by '{referencer}' through the '{edge.Name}' edge, but no record exists for it"));
            }
        }
    }

    /// <summary>
    /// Every reference through a relationship field the ontology does not
    /// model — owners pointing at teams and roles, the shared risk library's
    /// <c>ARR-RISK-044</c>, and the other fields the slice declares unmodeled —
    /// is a finding: the reference is recorded, never traversable, and the
    /// referenced entity is outside the ingested world.
    /// </summary>
    private static void LintUnmodeledReferences(TypedPropertyGraph graph, List<LintFinding> findings)
    {
        foreach (var reference in graph.UnmodeledReferences)
        {
            findings.Add(new LintFinding(
                reference.TargetId,
                UnmodeledReferenceInvariant,
                ValidationState.Unknown,
                $"referenced by '{reference.FromId}' through field '{reference.FieldPath}' — {reference.Reason}"));
        }
    }

    /// <summary>
    /// Every exception must expire, and an expiry within the warning window is
    /// a finding: an exception without an expiry date tends to become
    /// permanent, an expired one silently normalizes the deviation it covered,
    /// and an expiring one needs its review scheduled.
    /// </summary>
    private static void LintExceptionExpiry(
        TypedPropertyGraph graph,
        List<LintFinding> findings,
        DateTime referenceDate)
    {
        foreach (var node in graph.GetNodes(NodeTypeOf(graph, "ControlException")))
        {
            if (StringProperty(node, "expires") is not { } expires)
            {
                findings.Add(new LintFinding(
                    node.Id,
                    ExceptionExpiryInvariant,
                    ValidationState.Unknown,
                    "declares no expiry date, and an exception without one tends to become permanent"));
            }
            else if (!TryParseDate(expires, out var expiry))
            {
                findings.Add(new LintFinding(
                    node.Id,
                    ExceptionExpiryInvariant,
                    ValidationState.Unknown,
                    $"expiry date '{expires}' is not a yyyy-MM-dd date"));
            }
            else if (expiry < referenceDate)
            {
                findings.Add(new LintFinding(
                    node.Id,
                    ExceptionExpiryInvariant,
                    ValidationState.Expired,
                    $"expired on {expiry:yyyy-MM-dd}"));
            }
            else if (expiry - referenceDate <= ExceptionWarningWindow)
            {
                findings.Add(new LintFinding(
                    node.Id,
                    ExceptionExpiryInvariant,
                    ValidationState.Exception,
                    $"expires on {expiry:yyyy-MM-dd}, within 30 days of the reference date"));
            }
        }
    }

    /// <summary>
    /// Every supplier must carry a current risk assessment. A
    /// <c>last_risk_assessment</c> date older than the allowed age is stale; a
    /// supplier with no assessment date at all is a finding.
    /// </summary>
    private static void LintSupplierAssessmentFreshness(
        TypedPropertyGraph graph,
        List<LintFinding> findings,
        DateTime referenceDate)
    {
        foreach (var node in graph.GetNodes(NodeTypeOf(graph, "Supplier")))
        {
            if (StringProperty(node, "last_risk_assessment") is not { } assessed)
            {
                findings.Add(new LintFinding(
                    node.Id,
                    SupplierAssessmentFreshnessInvariant,
                    ValidationState.Unknown,
                    "declares no last_risk_assessment date"));
            }
            else if (!TryParseDate(assessed, out var assessment))
            {
                findings.Add(new LintFinding(
                    node.Id,
                    SupplierAssessmentFreshnessInvariant,
                    ValidationState.Unknown,
                    $"last_risk_assessment '{assessed}' is not a yyyy-MM-dd date"));
            }
            else if (referenceDate - assessment > SupplierAssessmentMaxAge)
            {
                findings.Add(new LintFinding(
                    node.Id,
                    SupplierAssessmentFreshnessInvariant,
                    ValidationState.Stale,
                    $"last_risk_assessment {assessment:yyyy-MM-dd} is {(referenceDate - assessment).Days} days before the reference date (allowed: 60)"));
            }
        }
    }

    /// <summary>
    /// A regulatory requirement whose satisfaction chain reaches a supplier
    /// must map to that supplier. The check applies only to requirements whose
    /// chain actually reaches one; for those, a missing direct mapping is a
    /// finding — the requirement depends on the supplier's risk posture, and
    /// the graph does not say so.
    /// </summary>
    private static void LintRequirementSupplierMappings(TypedPropertyGraph graph, List<LintFinding> findings)
    {
        foreach (var node in graph.GetNodes(NodeTypeOf(graph, "RegulatoryRequirement")))
        {
            var suppliers = Reachable(graph, node, candidate => candidate.Type.Name == "Supplier");
            if (suppliers.Count == 0)
            {
                // No supplier is in the requirement's satisfaction domain: the
                // check does not apply to this requirement.
                continue;
            }

            var supplierIds = suppliers.Select(supplier => supplier.Id).ToArray();
            var mapped = graph.GetEdges(node.Id).Any(edge =>
                (edge.FromId == node.Id && supplierIds.Contains(edge.ToId, StringComparer.Ordinal))
                || (edge.ToId == node.Id && supplierIds.Contains(edge.FromId, StringComparer.Ordinal)));
            if (!mapped)
            {
                findings.Add(new LintFinding(
                    node.Id,
                    RequirementSupplierMappingInvariant,
                    ValidationState.Unknown,
                    $"its satisfaction chain reaches {string.Join(", ", supplierIds)}, but the requirement declares no mapping to {(supplierIds.Length == 1 ? "that supplier" : "those suppliers")}"));
            }
        }
    }

    /// <summary>
    /// Every declared-versus-observed reconciliation the graph records as
    /// <see cref="ValidationState.Conflicting"/> is a finding: both values
    /// disagree and both sides stay visible — the conflict is surfaced, never
    /// silently resolved.
    /// </summary>
    private static void LintReconciliationConflicts(TypedPropertyGraph graph, List<LintFinding> findings)
    {
        foreach (var reconciliation in graph.Reconciliations.Where(reconciliation => reconciliation.IsConflicting))
        {
            findings.Add(new LintFinding(
                reconciliation.SubjectId,
                DeclaredObservedReconciliationInvariant,
                ValidationState.Conflicting,
                $"property '{reconciliation.PropertyName}': declared {reconciliation.DeclaredValue.ToJsonString()} differs from observed {reconciliation.ObservedValue.ToJsonString()} ({reconciliation.ClaimType} claim, {reconciliation.Precedence} precedence)"));
        }
    }

    /// <summary>
    /// Every recorded observation whose result is <see cref="ValidationState.Fail"/>
    /// is a finding: a failed validation stays failed in the store and must
    /// stay failed here.
    /// </summary>
    private static void LintObservationResults(
        TypedPropertyGraph graph,
        ObservationStore store,
        List<LintFinding> findings)
    {
        foreach (var assertion in graph.GetNodes(NodeTypeOf(graph, "Assertion")))
        {
            foreach (var observation in store.Timeline(assertion.Id))
            {
                if (observation.RecordedState != ValidationState.Fail)
                {
                    continue;
                }

                var reason = observation.ObservationId is { } observationId
                    ? $"observation {observationId} recorded FAIL against {assertion.Id}"
                    : $"evaluation against {assertion.Id} failed for this subject";
                if (observation.RelatedIncident is { } incident)
                {
                    reason += $"; related incident {incident}";
                }

                findings.Add(new LintFinding(
                    observation.SubjectId,
                    ObservationResultInvariant,
                    ValidationState.Fail,
                    reason));
            }
        }
    }

    /// <summary>
    /// Every stored observation produced by a validator version older than its
    /// assertion's current validator is stale evidence and a finding: the
    /// observation stays queryable, but it is visibly outdated rather than
    /// smoothed into current evidence.
    /// </summary>
    private static void LintObservationValidatorStaleness(
        TypedPropertyGraph graph,
        ObservationStore store,
        List<LintFinding> findings)
    {
        foreach (var assertion in graph.GetNodes(NodeTypeOf(graph, "Assertion")))
        {
            var currentValidator = store.CurrentValidatorVersion(assertion.Id);
            foreach (var observation in store.Timeline(assertion.Id))
            {
                if (!observation.IsStale)
                {
                    continue;
                }

                var producedBy = observation.ObservationId is { } observationId
                    ? $"observation {observationId} was produced by {observation.ValidatorVersion}"
                    : $"produced by {observation.ValidatorVersion}";
                findings.Add(new LintFinding(
                    observation.SubjectId,
                    ObservationValidatorStalenessInvariant,
                    ValidationState.Stale,
                    $"{producedBy}, behind the assertion's current validator {currentValidator}"));
            }
        }
    }

    /// <summary>
    /// Whether any typed edge chain from <paramref name="start"/> reaches a
    /// node matching <paramref name="target"/>.
    /// </summary>
    private static bool Reaches(TypedPropertyGraph graph, GraphNode start, Func<GraphNode, bool> target) =>
        Walk(graph, start).Any(target);

    /// <summary>
    /// Every node reachable from <paramref name="start"/> matching
    /// <paramref name="target"/>, in first-reached order.
    /// </summary>
    private static List<GraphNode> Reachable(
        TypedPropertyGraph graph,
        GraphNode start,
        Func<GraphNode, bool> target) =>
        Walk(graph, start).Where(target).ToList();

    /// <summary>
    /// The nodes reachable from <paramref name="start"/> through any typed
    /// edge chain, in breadth-first order over the edges in the graph's
    /// insertion order — deterministic, cycle-safe through the visited set,
    /// and never crossing an unresolved edge (a missing endpoint is not a
    /// node).
    /// </summary>
    private static IEnumerable<GraphNode> Walk(TypedPropertyGraph graph, GraphNode start)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { start.Id };
        var queue = new Queue<GraphNode>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var edge in graph.GetEdges(current.Id))
            {
                var otherId = edge.FromId == current.Id ? edge.ToId : edge.FromId;
                if (!graph.TryGetNode(otherId, out var other) || !visited.Add(otherId))
                {
                    continue;
                }

                yield return other;
                queue.Enqueue(other);
            }
        }
    }

    private static NodeType NodeTypeOf(TypedPropertyGraph graph, string name) =>
        graph.Registry.NodeTypes.Single(type => type.Name == name);

    private static bool IsClassifiedProduction(GraphNode node) =>
        StringProperty(node, "classification") is { } classification
        && string.Equals(classification, ProductionClassification, StringComparison.Ordinal);

    private static string? OwnerText(GraphNode node) =>
        StringProperty(node, "owner") ?? StringProperty(node, "owned_by");

    private static string? StringProperty(GraphNode node, string name) =>
        node.Properties.TryGetValue(name, out var value)
        && value is JsonValue jsonValue
        && jsonValue.TryGetValue<string>(out var text)
            ? text
            : null;

    private static bool TryParseDate(string text, out DateTime date) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
