using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Adapters;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ingestion;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Linting;
using GraphBasedComplianceTraversal.Engine.Observations;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;
using GraphBasedComplianceTraversal.Engine.Reconciliation;
using GraphBasedComplianceTraversal.Engine.State;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Pins the organizational linter: the structural checks and graph-health
/// checks over the reconciled world produce deterministic, ordered findings
/// naming the subject, the failed invariant, and the resulting state — and
/// missing information is never read as success. The scenarios run the full
/// pipeline over the repository's own world (golden-pinned, with each of the
/// six seeded imperfections reported as a distinct finding) and over synthetic
/// graphs for the negative paths: an artifact with no traversable path to a
/// source repository, structural gaps whose supporting facts are merely
/// absent, expired exceptions, and corroborated-versus-conflicting facts.
/// </summary>
public sealed class LinterTests
{
    private static readonly DateTimeOffset ReferenceTimestamp =
        DateTimeOffset.Parse("2026-08-12T09:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void TheRealWorldFindingsAreDeterministicAndMatchTheGoldenFile()
    {
        var graph = LoadReconciledWorld();
        var store = LoadHistoryStore();

        var first = OrganizationalLinter.Lint(graph, store, ReferenceTimestamp);
        var second = OrganizationalLinter.Lint(graph, store, ReferenceTimestamp);

        // Same inputs, same findings — the linter never reads the clock or
        // any other live state.
        Assert.Equal(first, second);
        Golden.AssertMatches("linter-findings.txt", Render(first));
    }

    [Fact]
    public void EachOfTheSixSeededImperfectionsIsReportedAsADistinctFinding()
    {
        var findings = LintRealWorld();

        // #1 — the failed provenance observation: the blocked unsigned
        // upload stays a FAIL finding, naming its incident.
        var failure = Assert.Single(findings, finding => finding.Invariant == "observation-result");
        Assert.Equal(ValidationState.Fail, failure.State);
        Assert.Equal("artifact:customer-agent-2.8.4-unsigned", failure.SubjectId);
        Assert.Contains("observation:obs-2026-08-06-001", failure.Reason, StringComparison.Ordinal);
        Assert.Contains("INC-2026-041", failure.Reason, StringComparison.Ordinal);

        // #2 — the stale observation: produced by validator 3.1.5, behind
        // the assertion's current 3.1.7. The 3.1.6 observation is one
        // version behind too, so it is surfaced stale as well.
        var staleObservations = findings
            .Where(finding => finding.Invariant == "observation-validator-staleness")
            .ToArray();
        Assert.Equal(2, staleObservations.Length);
        var behind = Assert.Single(staleObservations, finding => finding.SubjectId == "artifact:customer-agent-2.8.1");
        Assert.Equal(ValidationState.Stale, behind.State);
        Assert.Contains("observation:obs-2026-07-15-001", behind.Reason, StringComparison.Ordinal);
        Assert.Contains("provenance-prober@3.1.5", behind.Reason, StringComparison.Ordinal);
        Assert.Contains("provenance-prober@3.1.7", behind.Reason, StringComparison.Ordinal);

        // #3 — the expiring emergency-deployment exception: still covered,
        // but expiring 2026-09-01, within the 30-day warning window.
        var exception = Assert.Single(findings, finding => finding.Invariant == "exception-expiry");
        Assert.Equal(ValidationState.Exception, exception.State);
        Assert.Equal("exception:emergency-deployment-path", exception.SubjectId);
        Assert.Contains("2026-09-01", exception.Reason, StringComparison.Ordinal);

        // #4 — the supplier's stale risk assessment: 2026-06-01 is more
        // than 60 days before the reference date.
        var supplier = Assert.Single(findings, finding => finding.Invariant == "supplier-assessment-freshness");
        Assert.Equal(ValidationState.Stale, supplier.State);
        Assert.Equal("supplier:aws", supplier.SubjectId);
        Assert.Contains("2026-06-01", supplier.Reason, StringComparison.Ordinal);

        // #5 — the requirement's missing supplier mapping: its satisfaction
        // chain reaches supplier:aws, but the requirement declares no
        // mapping to that supplier.
        var mapping = Assert.Single(findings, finding => finding.Invariant == "requirement-supplier-mapping");
        Assert.Equal(ValidationState.Unknown, mapping.State);
        Assert.Equal("requirement:nis2-article-21-supply-chain", mapping.SubjectId);
        Assert.Contains("supplier:aws", mapping.Reason, StringComparison.Ordinal);

        // #6 — the unresolved generic risk reference ARR-RISK-044, from a
        // shared risk library that is not part of the ingested world.
        var genericRisk = Assert.Single(findings, finding => finding.SubjectId == "ARR-RISK-044");
        Assert.Equal(ValidationState.Unknown, genericRisk.State);
        Assert.Contains("generic_risk", genericRisk.Reason, StringComparison.Ordinal);
        Assert.Contains("risk:artifact-tampering", genericRisk.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRealWorldFindingsCoverEveryCheckTheWorldTrips()
    {
        var findings = LintRealWorld();

        // The two production cloud resources declare no owner: two UNKNOWN
        // findings — absence is surfaced, never read as success.
        Assert.Equal(
            [
                ("aws:cloudfront:customer-downloads", ValidationState.Unknown),
                ("aws:s3:prod-release-artifacts", ValidationState.Unknown),
            ],
            findings
                .Where(finding => finding.Invariant == "production-resource-owner")
                .Select(finding => (finding.SubjectId, finding.State))
                .ToArray());

        // Six unresolved typed edges (two capabilities without records, four
        // observation subjects naming artifact versions without records) and
        // every retained unmodeled reference are reference findings.
        Assert.Equal(6, findings.Count(finding => finding.Invariant == "unresolved-reference"));
        Assert.Equal(29, findings.Count(finding => finding.Invariant == "unmodeled-reference"));
        Assert.Contains(
            findings,
            finding => finding.Invariant == "unresolved-reference" && finding.SubjectId == "capability:endpoint-telemetry-collection");
        Assert.Contains(
            findings,
            finding => finding.Invariant == "unmodeled-reference" && finding.SubjectId == "team:platform-engineering");

        // The two real declared-versus-observed disagreements — the
        // repository's description and owner — surface as CONFLICTING.
        Assert.Equal(2, findings.Count(finding => finding.Invariant == "declared-observed-reconciliation"));
        Assert.All(
            findings.Where(finding => finding.Invariant == "declared-observed-reconciliation"),
            finding =>
            {
                Assert.Equal(ValidationState.Conflicting, finding.State);
                Assert.Equal("repository:customer-agent", finding.SubjectId);
            });

        // The checks the fixture world satisfies — the risk has a treatment,
        // the adoption an implementation, the implementation a validating
        // assertion, and the artifact a path to its source repository —
        // produce no findings: a passing check is silent, not a report.
        Assert.DoesNotContain(findings, finding => finding.Invariant == "risk-treatment");
        Assert.DoesNotContain(findings, finding => finding.Invariant == "adoption-implementation");
        Assert.DoesNotContain(findings, finding => finding.Invariant == "implementation-assertion");
        Assert.DoesNotContain(findings, finding => finding.Invariant == "artifact-source-path");
    }

    [Fact]
    public void AnArtifactWithNoDerivationFactIsFlaggedUnknownRatherThanPassed()
    {
        var graph = EmptyGraph();
        graph.AddNode("artifact:orphaned", Node("Artifact"));

        var finding = Assert.Single(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));

        Assert.Equal("artifact:orphaned", finding.SubjectId);
        Assert.Equal("artifact-source-path", finding.Invariant);
        Assert.Equal(ValidationState.Unknown, finding.State);
        Assert.Contains("produced-by", finding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArtifactWhoseDerivationChainDoesNotReachASourceRepositoryFails()
    {
        var graph = EmptyGraph();
        graph.AddNode("artifact:detached", Node("Artifact"));
        graph.AddNode("build-pipeline:no-source", Node("BuildPipeline"));
        graph.AddEdge("produced-by", "artifact:detached", "build-pipeline:no-source");

        var finding = Assert.Single(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));

        Assert.Equal("artifact:detached", finding.SubjectId);
        Assert.Equal("artifact-source-path", finding.Invariant);
        Assert.Equal(ValidationState.Fail, finding.State);
        Assert.Contains("no traversable path", finding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArtifactWithATraversablePathToItsSourceRepositoryProducesNoFinding()
    {
        var graph = EmptyGraph();
        graph.AddNode("artifact:traceable", Node("Artifact"));
        graph.AddNode("build-pipeline:pipeline", Node("BuildPipeline"));
        graph.AddNode("repository:source", Node("SourceRepository"));
        graph.AddEdge("produced-by", "artifact:traceable", "build-pipeline:pipeline");
        graph.AddEdge("source", "build-pipeline:pipeline", "repository:source");

        Assert.Empty(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));
    }

    [Fact]
    public void AProductionResourceWithoutAnOwnerIsFlaggedUnknownNotPassed()
    {
        var graph = EmptyGraph();
        graph.AddNode("aws:s3:ownerless", Node("S3Bucket"), ProductionProperties());

        var finding = Assert.Single(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));

        Assert.Equal("aws:s3:ownerless", finding.SubjectId);
        Assert.Equal("production-resource-owner", finding.Invariant);
        Assert.Equal(ValidationState.Unknown, finding.State);
    }

    [Fact]
    public void AProductionResourceWithAnOwnerProducesNoFinding()
    {
        var graph = EmptyGraph();
        graph.AddNode("aws:s3:owned", Node("S3Bucket"), ProductionProperties("owner", "role:platform-security"));

        Assert.Empty(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));
    }

    [Fact]
    public void AProductionResourceWithAnEmptyOwnerFails()
    {
        var graph = EmptyGraph();
        graph.AddNode("aws:s3:empty-owner", Node("S3Bucket"), ProductionProperties("owner", " "));

        var finding = Assert.Single(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));

        Assert.Equal(ValidationState.Fail, finding.State);
        Assert.Equal("production-resource-owner", finding.Invariant);
    }

    [Fact]
    public void AnOwnerIsOnlyRequiredOfProductionResources()
    {
        var graph = EmptyGraph();
        graph.AddNode("aws:s3:staging", Node("S3Bucket"));

        Assert.Empty(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));
    }

    [Fact]
    public void ARiskWithoutATreatmentIsFlaggedUnknownNotPassed()
    {
        var graph = EmptyGraph();
        graph.AddNode("risk:untreated", Node("Risk"));

        var finding = Assert.Single(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));

        Assert.Equal("risk:untreated", finding.SubjectId);
        Assert.Equal("risk-treatment", finding.Invariant);
        Assert.Equal(ValidationState.Unknown, finding.State);
    }

    [Fact]
    public void ARiskWhoseTreatmentNamesANodeWithNoRecordFails()
    {
        var graph = EmptyGraph();
        graph.AddNode("risk:broken-treatment", Node("Risk"));
        graph.AddEdge(
            "mitigated-by",
            "risk:broken-treatment",
            Node("Risk"),
            "control-adoption:missing",
            Node("ControlAdoption"));

        var findings = OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp);

        // The broken treatment is one finding; the missing adoption record is
        // the other (an unresolved reference in its own right).
        var finding = Assert.Single(findings, finding => finding.Invariant == "risk-treatment");
        Assert.Equal(ValidationState.Fail, finding.State);
        Assert.Contains("control-adoption:missing", finding.Reason, StringComparison.Ordinal);
        Assert.Contains(
            findings,
            other => other.Invariant == "unresolved-reference" && other.SubjectId == "control-adoption:missing");
    }

    [Fact]
    public void ARiskWithAResolvedTreatmentProducesNoFinding()
    {
        // A fully treated world: the risk is mitigated by an adoption that is
        // itself implemented and validated, so no check trips.
        var graph = EmptyGraph();
        graph.AddNode("risk:treated", Node("Risk"));
        graph.AddNode("control-adoption:adopted", Node("ControlAdoption"));
        graph.AddNode("implementation:implemented", Node("ControlImplementation"));
        graph.AddNode("assertion:validating", Node("Assertion"));
        graph.AddEdge("mitigated-by", "risk:treated", "control-adoption:adopted");
        graph.AddEdge("implemented-by", "control-adoption:adopted", "implementation:implemented");
        graph.AddEdge("validates", "assertion:validating", "implementation:implemented");

        Assert.Empty(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));
    }

    [Fact]
    public void AnAdoptionWithoutAnImplementationIsFlaggedUnknownNotPassed()
    {
        var graph = EmptyGraph();
        graph.AddNode("control-adoption:unimplemented", Node("ControlAdoption"));

        var finding = Assert.Single(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));

        Assert.Equal("control-adoption:unimplemented", finding.SubjectId);
        Assert.Equal("adoption-implementation", finding.Invariant);
        Assert.Equal(ValidationState.Unknown, finding.State);
    }

    [Fact]
    public void AnImplementationWithoutAValidatingAssertionIsFlaggedUnknownNotPassed()
    {
        var graph = EmptyGraph();
        graph.AddNode("implementation:unvalidated", Node("ControlImplementation"));

        var finding = Assert.Single(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));

        Assert.Equal("implementation:unvalidated", finding.SubjectId);
        Assert.Equal("implementation-assertion", finding.Invariant);
        Assert.Equal(ValidationState.Unknown, finding.State);
    }

    [Fact]
    public void AnExpiredExceptionIsFlaggedExpired()
    {
        var graph = EmptyGraph();
        graph.AddNode("exception:lapsed", Node("ControlException"), Properties("expires", "2026-01-01"));

        var finding = Assert.Single(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));

        Assert.Equal("exception:lapsed", finding.SubjectId);
        Assert.Equal("exception-expiry", finding.Invariant);
        Assert.Equal(ValidationState.Expired, finding.State);
    }

    [Fact]
    public void AnExceptionWithoutAnExpiryDateIsFlaggedUnknownNotPassed()
    {
        var graph = EmptyGraph();
        graph.AddNode("exception:permanent", Node("ControlException"));

        var finding = Assert.Single(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));

        Assert.Equal(ValidationState.Unknown, finding.State);
        Assert.Contains("no expiry date", finding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExceptionWithADistantExpiryProducesNoFinding()
    {
        var graph = EmptyGraph();
        graph.AddNode("exception:healthy", Node("ControlException"), Properties("expires", "2027-09-01"));

        Assert.Empty(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));
    }

    [Fact]
    public void ASupplierWithoutARiskAssessmentIsFlaggedUnknownNotPassed()
    {
        var graph = EmptyGraph();
        graph.AddNode("supplier:unassessed", Node("Supplier"));

        var finding = Assert.Single(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));

        Assert.Equal("supplier:unassessed", finding.SubjectId);
        Assert.Equal("supplier-assessment-freshness", finding.Invariant);
        Assert.Equal(ValidationState.Unknown, finding.State);
    }

    [Fact]
    public void ASupplierWithAFreshRiskAssessmentProducesNoFinding()
    {
        var graph = EmptyGraph();
        graph.AddNode("supplier:current", Node("Supplier"), Properties("last_risk_assessment", "2026-07-01"));

        Assert.Empty(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));
    }

    [Fact]
    public void ARequirementWhoseSatisfactionChainReachesASupplierMustMapToIt()
    {
        var graph = EmptyGraph();
        graph.AddNode("requirement:supply-chain", Node("RegulatoryRequirement"));
        graph.AddNode("control-adoption:adoption", Node("ControlAdoption"));
        graph.AddNode("aws:s3:bucket", Node("S3Bucket"));
        graph.AddNode("service:distribution", Node("Service"));
        graph.AddNode("supplier:aws", Node("Supplier"));
        graph.AddEdge("satisfies", "control-adoption:adoption", "requirement:supply-chain");
        graph.AddEdge("applies-to", "control-adoption:adoption", "aws:s3:bucket");
        graph.AddEdge("depends-on", "service:distribution", "aws:s3:bucket");
        graph.AddEdge("supplies", "supplier:aws", "service:distribution");

        var findings = OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp);

        // The missing mapping is the check under test; the synthetic world
        // also trips the adoption-implementation and supplier-assessment
        // checks, which produce their own findings.
        var finding = Assert.Single(findings, finding => finding.Invariant == "requirement-supplier-mapping");
        Assert.Equal("requirement:supply-chain", finding.SubjectId);
        Assert.Equal(ValidationState.Unknown, finding.State);
        Assert.Contains("supplier:aws", finding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ARequirementWithNoSupplierInItsSatisfactionChainProducesNoMappingFinding()
    {
        var graph = EmptyGraph();
        graph.AddNode("requirement:internal", Node("RegulatoryRequirement"));

        Assert.Empty(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));
    }

    [Fact]
    public void AnEdgeWhoseTargetHasNoRecordIsAnUnresolvedReferenceFinding()
    {
        var graph = EmptyGraph();
        graph.AddNode("product:p", Node("Product"));
        graph.AddEdge("has-capability", "product:p", Node("Product"), "capability:missing", Node("ProductCapability"));

        var finding = Assert.Single(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));

        Assert.Equal("capability:missing", finding.SubjectId);
        Assert.Equal("unresolved-reference", finding.Invariant);
        Assert.Equal(ValidationState.Unknown, finding.State);
        Assert.Contains("product:p", finding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AReferenceThroughAnUnmodeledFieldIsAnUnmodeledReferenceFinding()
    {
        var graph = EmptyGraph();
        graph.AddNode("risk:generic", Node("Risk"));
        graph.RecordUnmodeledReference("risk:generic", "generic_risk", "ARR-RISK-044");

        var findings = OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp);

        // The untreatable reference is one finding; the risk's missing
        // treatment is the other.
        var finding = Assert.Single(findings, finding => finding.Invariant == "unmodeled-reference");
        Assert.Equal("ARR-RISK-044", finding.SubjectId);
        Assert.Equal(ValidationState.Unknown, finding.State);
        Assert.Contains("'generic_risk'", finding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ACorroboratedReconciliationProducesNoFindingWhileAConflictDoes()
    {
        var graph = EmptyGraph();
        graph.AddNode("aws:s3:agreeing", Node("S3Bucket"));
        graph.AddNode("aws:s3:disagreeing", Node("S3Bucket"));

        var declared = JsonValue.Create("aws:kms");
        var observed = JsonValue.Create("AES256");
        var declaration = new FactProvenance(new ProvenanceRecord(SourceKind.Declaration, "data/x.yaml"));
        var awsObserved = new FactProvenance(new ProvenanceRecord(SourceKind.AwsObserved, "fixtures/aws/x.json"));

        graph.RecordReconciliation(new FactReconciliation(
            "aws:s3:agreeing",
            "encryption",
            ValidationState.Pass,
            declared,
            declaration,
            declared,
            awsObserved,
            ClaimType.Configuration,
            SourcePrecedence.RightWins,
            new ProvenanceRecord(SourceKind.AwsObserved, "fixtures/aws/x.json")));
        graph.RecordReconciliation(new FactReconciliation(
            "aws:s3:disagreeing",
            "encryption",
            ValidationState.Conflicting,
            declared,
            declaration,
            observed,
            awsObserved,
            ClaimType.Configuration,
            SourcePrecedence.RightWins,
            new ProvenanceRecord(SourceKind.AwsObserved, "fixtures/aws/x.json")));

        // Only the disagreement is a finding; corroboration is agreement, not
        // a gap — but it is never confused with absence, which the other
        // checks surface as UNKNOWN.
        var finding = Assert.Single(OrganizationalLinter.Lint(graph, new ObservationStore(), ReferenceTimestamp));
        Assert.Equal("aws:s3:disagreeing", finding.SubjectId);
        Assert.Equal("declared-observed-reconciliation", finding.Invariant);
        Assert.Equal(ValidationState.Conflicting, finding.State);
        Assert.Contains("'encryption'", finding.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AFindingIsOnlyCreatedForANonPassingState()
    {
        var graph = EmptyGraph();
        graph.AddNode("aws:s3:b", Node("S3Bucket"));

        // A finding is only produced when a check does not pass, and only a
        // declared validation state can be recorded.
        Assert.Throws<ArgumentOutOfRangeException>(() => new LintFinding(
            "aws:s3:b", "production-resource-owner", ValidationState.Pass, "why"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LintFinding(
            "aws:s3:b", "production-resource-owner", (ValidationState)int.MaxValue, "why"));
        Assert.Throws<ArgumentException>(() => new LintFinding(
            " ", "production-resource-owner", ValidationState.Unknown, "why"));
        Assert.Throws<ArgumentException>(() => new LintFinding(
            "aws:s3:b", " ", ValidationState.Unknown, "why"));
        Assert.Throws<ArgumentException>(() => new LintFinding(
            "aws:s3:b", "production-resource-owner", ValidationState.Unknown, " "));
    }

    private static IReadOnlyList<LintFinding> LintRealWorld() =>
        OrganizationalLinter.Lint(LoadReconciledWorld(), LoadHistoryStore(), ReferenceTimestamp);

    private static TypedPropertyGraph LoadReconciledWorld()
    {
        var world = RepositoryDataLoader.Load(TestWorld.RepositoryRoot);
        var graph = new TypedPropertyGraph(EdgeTypeRegistry.Slice);
        DeclaredRecordsLoader.ApplyAll(graph, world.Records);
        DeclaredObservedReconciler.ReconcileAll(graph, FixtureAdapterRegistry.MockWorld.NormalizeAll(world.Fixtures));
        return graph;
    }

    private static ObservationStore LoadHistoryStore()
    {
        var store = new ObservationStore();
        HistoricalObservationLoader.ApplyAll(store, RepositoryDataLoader.Load(TestWorld.RepositoryRoot).Records);
        return store;
    }

    private static TypedPropertyGraph EmptyGraph() => new(EdgeTypeRegistry.Slice);

    private static NodeType Node(string name) =>
        EdgeTypeRegistry.Slice.NodeTypes.Single(type => type.Name == name);

    private static Dictionary<string, JsonNode?> ProductionProperties() =>
        Properties("classification", "production");

    private static Dictionary<string, JsonNode?> ProductionProperties(string name, string value)
    {
        var properties = ProductionProperties();
        properties[name] = JsonValue.Create(value);
        return properties;
    }

    private static Dictionary<string, JsonNode?> Properties(string name, string value) => new()
    {
        [name] = JsonValue.Create(value),
    };

    private static string Render(IReadOnlyList<LintFinding> findings)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"Organizational linter — reference {ReferenceTimestamp:O}, {findings.Count} findings");
        foreach (var finding in findings)
        {
            builder.Append(finding.State.ToString().ToUpperInvariant().PadRight(12));
            builder.Append(finding.Invariant.PadRight(32));
            builder.Append(finding.SubjectId.PadRight(64));
            builder.AppendLine(finding.Reason);
        }

        return builder.ToString();
    }
}
