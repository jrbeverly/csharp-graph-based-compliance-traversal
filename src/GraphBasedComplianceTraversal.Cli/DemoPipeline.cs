using System.Globalization;
using GraphBasedComplianceTraversal.Engine.Adapters;
using GraphBasedComplianceTraversal.Engine.Assertions;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ingestion;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Linting;
using GraphBasedComplianceTraversal.Engine.Narrative;
using GraphBasedComplianceTraversal.Engine.Observations;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Reconciliation;
using GraphBasedComplianceTraversal.Engine.State;
using GraphBasedComplianceTraversal.Engine.Traversal;

namespace GraphBasedComplianceTraversal.Cli;

/// <summary>
/// The end-to-end demonstration the planning input exists to make possible:
/// one offline, deterministic run that loads the repository's declared facts
/// and mocked external reality, builds the typed graph, traverses the three
/// canonical cross-domain paths, evaluates the assertion slice, lints the
/// reconciled structure, and compiles the explainable supply-chain narrative —
/// with the fixtures' seeded imperfections left visible, never smoothed into
/// a perfect story. <see cref="Render"/> is the whole pipeline; every section
/// is templated from the engine's own deterministic renderings, the reference
/// timestamp is a fixed constant (the fixture world's reference date, never
/// the clock), and no network or credential is ever consulted — re-running
/// against the unchanged repository produces byte-identical output, pinned as
/// the <c>demo.txt</c> golden. See <c>docs/demo.md</c>.
/// </summary>
public static class DemoPipeline
{
    /// <summary>
    /// The fixed reference timestamp every time-dependent stage evaluates
    /// against: the fixture world's reference date (the observation history
    /// ends 2026-08-08, the exception expires 2026-09-01, the supplier
    /// assessment is dated 2026-06-01). The demonstration never reads the
    /// clock — a run today and a run next month produce the same output.
    /// </summary>
    public static readonly DateTimeOffset ReferenceTimestamp =
        DateTimeOffset.Parse("2026-08-12T09:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>
    /// Runs the demonstration over the repository at <paramref name="repositoryRoot"/>,
    /// writing the rendered pipeline to <paramref name="output"/>. Throws when
    /// the repository cannot be loaded; the CLI entry point reports the
    /// failure with a non-zero exit code.
    /// </summary>
    public static void Run(string repositoryRoot, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Write(Render(repositoryRoot));
    }

    /// <summary>
    /// The full pipeline as one deterministic text document, in pipeline
    /// order: facts loaded, mocked observations normalized, the typed graph,
    /// the three canonical paths, assertion evaluation, the observation
    /// store's current state, the linter findings, and the compiled narrative.
    /// </summary>
    public static string Render(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var world = RepositoryDataLoader.Load(repositoryRoot);
        var graph = new TypedPropertyGraph(EdgeTypeRegistry.Slice);
        DeclaredRecordsLoader.ApplyAll(graph, world.Records);
        var normalized = FixtureAdapterRegistry.MockWorld.NormalizeAll(world.Fixtures);
        DeclaredObservedReconciler.ReconcileAll(graph, normalized);

        var store = new ObservationStore();
        HistoricalObservationLoader.ApplyAll(store, world.Records);

        var evaluations = AssertionRegistry.Slice.EvaluateAll(graph, ReferenceTimestamp);
        var findings = OrganizationalLinter.Lint(graph, store, ReferenceTimestamp);
        var narrative = NarrativeRendering.Render(DomainNarratives.SupplyChainCompliance(graph, store));
        var canonicalPaths = ExecuteCanonicalPaths(graph);

        var sections = new List<string>
        {
            Header(),
            FactsLoaded(world),
            ObservationsNormalized(normalized),
            TypedGraph(graph),
            RenderCanonicalPaths(canonicalPaths),
            AssertionEvaluations(evaluations),
            ObservationState(store),
            LinterFindings(findings),
            CompiledNarrative(narrative),
            $"Demonstration complete — 3 canonical paths " +
            $"({canonicalPaths.Sum(path => path.Report.Paths.Count)} explained traversals), " +
            $"{evaluations.Count} assertion evaluations, {findings.Count} linter findings, 1 compiled narrative, " +
            "all offline and deterministic.",
        };

        return string.Join("\n\n", sections) + "\n";
    }

    private static string Header() =>
        "Graph-based compliance traversal — end-to-end demonstration\n" +
        $"Reference timestamp: {ReferenceTimestamp.ToString("O", CultureInfo.InvariantCulture)} " +
        "(fixed — the fixture world's reference date, never the clock)";

    private static string FactsLoaded(RepositoryData world) =>
        "1. Facts loaded (RepositoryDataLoader)\n" +
        $"   {world.Records.Count} declared records under data/\n" +
        $"   {world.Fixtures.Count} mocked external responses under fixtures/";

    private static string ObservationsNormalized(IReadOnlyList<NormalizedState> normalized) =>
        "2. Mocked observations normalized (FixtureAdapterRegistry.MockWorld)\n" +
        $"   {normalized.Sum(state => state.Nodes.Count)} normalized nodes and " +
        $"{normalized.Sum(state => state.Edges.Count)} normalized edges from " +
        $"{FixtureAdapterRegistry.MockWorld.Adapters.Count} adapters";

    private static string TypedGraph(TypedPropertyGraph graph)
    {
        var conflicting = graph.Reconciliations.Count(reconciliation => reconciliation.IsConflicting);
        return "3. Typed connected graph (TypedPropertyGraph)\n" +
            $"   {graph.Nodes.Count} nodes and {graph.Edges.Count} typed edges\n" +
            $"   {graph.GetUnresolvedEdges().Count} unresolved references and " +
            $"{graph.UnmodeledReferences.Count} unmodeled references retained, never dropped\n" +
            $"   {graph.Reconciliations.Count} declared-versus-observed reconciliations recorded, " +
            $"of which {conflicting} conflicting";
    }

    private static string RenderCanonicalPaths(IReadOnlyList<CanonicalPath> paths)
    {
        TraversalReport Get(string label) =>
            paths.Single(path => path.Label == label).Report;

        return "4. Canonical path 1 of 3 — business ↔ infrastructure\n\n" +
            "Resource to purpose (why the resource exists):\n" +
            ExplainedPath.Render(Get("resource-to-purpose")) + "\n\n" +
            "Purpose to technical systems (the reverse, gaps reported):\n" +
            ExplainedPath.Render(Get("purpose-to-technical-systems")) + "\n\n" +
            "5. Canonical path 2 of 3 — infrastructure → source and provenance\n\n" +
            "Declared build chain:\n" +
            ExplainedPath.Render(Get("build-and-source")) + "\n\n" +
            "Attested derivation (the provenance attestation's chain):\n" +
            ExplainedPath.Render(Get("attested-derivation")) + "\n\n" +
            "Distribution:\n" +
            ExplainedPath.Render(Get("distribution")) + "\n\n" +
            "6. Canonical path 3 of 3 — risk → control and observations\n\n" +
            ExplainedPath.Render(Get("risk-to-control-and-observations"));
    }

    /// <summary>
    /// Executes the three canonical cross-domain traversals, each as a gap
    /// report so an incomplete walk states its unresolved references instead
    /// of looking complete.
    /// </summary>
    private static IReadOnlyList<CanonicalPath> ExecuteCanonicalPaths(TypedPropertyGraph graph)
    {
        // Path 1 — business ↔ infrastructure: why the bucket exists, and the
        // reverse walk from the purpose that reports its gaps.
        var resourceToPurpose = CanonicalTraversals.ResourceToPurpose(graph).ExecuteWithGaps();
        var purposeToSystems = CanonicalTraversals.PurposeToTechnicalSystems(graph).ExecuteWithGaps();

        // Path 2 — infrastructure → source and provenance: the artifact's
        // declared build chain, the attested derivation the provenance
        // attestation binds it to, and its distribution.
        var buildAndSource = CanonicalTraversals.ArtifactBuildAndSource(graph).ExecuteWithGaps();
        var attested = CanonicalTraversals.ArtifactAttestedDerivation(graph).ExecuteWithGaps();
        var distribution = CanonicalTraversals.ArtifactDistribution(graph).ExecuteWithGaps();

        // Path 3 — risk → control and observations: why the risk matters and
        // how it is controlled, one path per observation produced.
        var riskToControl = CanonicalTraversals.RiskToControlAndObservations(graph).ExecuteWithGaps();

        return
        [
            new CanonicalPath("resource-to-purpose", resourceToPurpose),
            new CanonicalPath("purpose-to-technical-systems", purposeToSystems),
            new CanonicalPath("build-and-source", buildAndSource),
            new CanonicalPath("attested-derivation", attested),
            new CanonicalPath("distribution", distribution),
            new CanonicalPath("risk-to-control-and-observations", riskToControl),
        ];
    }

    /// <summary>One executed canonical traversal, labelled for the rendering.</summary>
    private sealed record CanonicalPath(string Label, TraversalReport Report);

    private static string AssertionEvaluations(IReadOnlyList<AssertionObservation> evaluations) =>
        "7. Assertion evaluation " +
        $"(AssertionRegistry.Slice, evaluated at {ReferenceTimestamp.ToString("O", CultureInfo.InvariantCulture)})\n" +
        string.Join('\n', evaluations.Select(evaluation =>
            $"   {evaluation.AssertionId} / {evaluation.SubjectId} → " +
            $"{evaluation.State.ToString().ToUpperInvariant()} — {evaluation.Reason}"));

    private static string ObservationState(ObservationStore store)
    {
        var lines = new List<string> { "8. Observation store — current state per assertion" };
        foreach (var assertion in AssertionRegistry.Slice.Assertions)
        {
            lines.Add($"   {assertion.Id}");
            var timeline = store.Timeline(assertion.Id);
            if (timeline.Count == 0)
            {
                lines.Add("      (no recorded history)");
                continue;
            }

            foreach (var observation in timeline)
            {
                lines.Add($"      {observation.ObservationId} → {StateName(observation.State)}" +
                    $" (recorded {StateName(observation.RecordedState)} " +
                    $"{observation.ObservedAt.ToString("O", CultureInfo.InvariantCulture)} " +
                    $"by {observation.ValidatorVersion}" +
                    (observation.RelatedIncident is { } incident ? $"; incident {incident}" : string.Empty) +
                    ")");
            }
        }

        return string.Join('\n', lines);
    }

    private static string LinterFindings(IReadOnlyList<LintFinding> findings)
    {
        var lines = new List<string> { "9. Organizational linter findings" };
        lines.Add(
            $"Organizational linter — reference {ReferenceTimestamp.ToString("O", CultureInfo.InvariantCulture)}, " +
            $"{findings.Count} findings");
        foreach (var finding in findings)
        {
            lines.Add(
                finding.State.ToString().ToUpperInvariant().PadRight(12) +
                finding.Invariant.PadRight(32) +
                finding.SubjectId.PadRight(64) +
                finding.Reason);
        }

        return string.Join('\n', lines);
    }

    private static string CompiledNarrative(string narrative) =>
        "10. Compiled supply-chain narrative\n\n" + narrative;

    private static string StateName(ValidationState state) =>
        state.ToString().ToLowerInvariant();
}
