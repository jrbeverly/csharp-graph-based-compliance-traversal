using GraphBasedComplianceTraversal.Engine.Adapters;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ingestion;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Narrative;
using GraphBasedComplianceTraversal.Engine.Observations;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Traversal;
using GraphBasedComplianceTraversal.Engine.Views;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Pins the explainable narrative compiler: the supply-chain compliance
/// conclusion compiled from the validated assurance paths into a narrative in
/// which every sentence maps to a step and its supporting facts, each with
/// provenance — and, when no valid path exists, a narrative that states the
/// gap explicitly and draws no conclusion. The default rendering is pinned
/// golden-file by golden-file as deterministic, AI-free output, and the
/// downstream renderer boundary is pinned as receiving only the compiled
/// structure: no graph, no store, no engine. The scenarios run against the
/// shared world — the repository's declared <c>data/**</c> records merged
/// with the observed <c>fixtures/**</c> facts, plus the observation store
/// fed from the same records.
/// </summary>
public sealed class NarrativeCompilerTests
{
    private static readonly string[] ObservationIds =
    [
        "observation:obs-2026-08-08-001",
        "observation:obs-2026-08-07-001",
        "observation:obs-2026-08-06-001",
        "observation:obs-2026-08-05-001",
        "observation:obs-2026-07-15-001",
    ];

    [Fact]
    public void TheSupplyChainComplianceConclusionIsACompiledNarrativeOfTheValidatedPaths()
    {
        var (graph, store) = LoadSharedWorld();

        var narrative = DomainNarratives.SupplyChainCompliance(graph, store);

        // Compiled from the grammar result: established, about the
        // requirement, over all five validated paths.
        Assert.Equal(NarrativeVerdict.Established, narrative.Verdict);
        Assert.Equal("requirement:nis2-article-21-supply-chain", narrative.SubjectId);
        Assert.Equal("supply-chain-assurance", narrative.GrammarName);
        Assert.Equal(5, narrative.PathCount);

        // The three shared steps of the assurance chain — satisfied-by,
        // implemented-by, validated-by — are the same stored edges in every
        // path, so they compile once; the produces step differs per
        // observation, so it compiles once per path.
        Assert.Equal(
            [
                ("requirement:nis2-article-21-supply-chain", "control-adoption:ca-artifact-supply-chain", "satisfied-by", "satisfies", EdgeDirection.Inverse),
                ("control-adoption:ca-artifact-supply-chain", "implementation:signed-release-pipeline", "implemented-by", "implemented-by", EdgeDirection.Forward),
                ("implementation:signed-release-pipeline", "assertion:prod-artifact-provenance", "validated-by", "validates", EdgeDirection.Inverse),
                ("assertion:prod-artifact-provenance", "observation:obs-2026-08-08-001", "produces", "produces", EdgeDirection.Forward),
                ("assertion:prod-artifact-provenance", "observation:obs-2026-08-07-001", "produces", "produces", EdgeDirection.Forward),
                ("assertion:prod-artifact-provenance", "observation:obs-2026-08-06-001", "produces", "produces", EdgeDirection.Forward),
                ("assertion:prod-artifact-provenance", "observation:obs-2026-08-05-001", "produces", "produces", EdgeDirection.Forward),
                ("assertion:prod-artifact-provenance", "observation:obs-2026-07-15-001", "produces", "produces", EdgeDirection.Forward),
            ],
            narrative.Steps.Select(step => (step.FromNodeId, step.ToNodeId, step.EdgeName, step.EdgeTypeName, step.Direction)).ToArray());

        // One sentence per compiled step, then the verdict sentence — every
        // step sentence maps to its step, and the verdict sentence rests on
        // the subject's facts.
        Assert.Equal(narrative.Steps.Count + 1, narrative.Statements.Count);
        for (var index = 0; index < narrative.Steps.Count; index++)
        {
            var statement = narrative.Statements[index];
            Assert.Same(narrative.Steps[index], Assert.Single(statement.Steps));
            Assert.NotEmpty(statement.Facts);
            Assert.Equal(statement.Facts, narrative.Steps[index].Facts);
        }

        var verdict = narrative.Statements[^1];
        Assert.Empty(verdict.Steps);
        Assert.Contains(
            verdict.Facts,
            fact => fact.Kind == NarrativeFactKind.Node && fact.NodeId == "requirement:nis2-article-21-supply-chain");

        // The narrative ends at the five observations, in path order.
        Assert.Equal(
            ObservationIds,
            narrative.Steps.Select(step => step.ToNodeId).Skip(3).ToArray());

        Golden.AssertMatches("narrative/supply-chain-compliance.txt", NarrativeRendering.Render(narrative));
    }

    [Fact]
    public void EverySentenceMapsToStepsAndFactsOfTheUnderlyingPaths()
    {
        var (graph, store) = LoadSharedWorld();

        var narrative = DomainNarratives.SupplyChainCompliance(graph, store);

        // The sentence-to-fact mapping is structural: every sentence rests
        // on at least one fact, every fact a step sentence rests on belongs
        // to that step's two nodes, and every statement's text is the
        // deterministic rendering of those facts — no sentence maps to
        // nothing, and no fact floats unattached to a sentence.
        foreach (var statement in narrative.Statements)
        {
            Assert.NotEmpty(statement.Facts);
            foreach (var fact in statement.Facts)
            {
                Assert.Contains(fact, narrative.Facts);
                if (statement.Steps.Count == 0)
                {
                    continue;
                }

                var step = Assert.Single(statement.Steps);
                Assert.Contains(fact.NodeId, new[] { step.FromNodeId, step.ToNodeId });
                if (fact.Kind == NarrativeFactKind.Edge)
                {
                    Assert.Equal(step.FromNodeId, fact.NodeId);
                    Assert.Equal(step.ToNodeId, fact.TargetNodeId);
                }
            }
        }

        // Every step of the underlying paths is present as a step of the
        // compiled narrative, and every compiled step carries the typed edge
        // it traversed with the edge's provenance.
        foreach (var step in narrative.Steps)
        {
            Assert.Contains(
                step.Facts,
                fact => fact.Kind == NarrativeFactKind.Edge
                    && fact.NodeId == step.FromNodeId
                    && fact.TargetNodeId == step.ToNodeId
                    && fact.Provenance.HasKnownSource);
        }

        // The shared satisfied-by edge is established by both of its
        // records: the requirement's and the adoption's.
        var satisfiedBy = narrative.Steps.Single(step => step.EdgeName == "satisfied-by");
        var edgeFact = satisfiedBy.Facts.Single(fact => fact.Kind == NarrativeFactKind.Edge);
        Assert.Equal(2, edgeFact.Provenance.Sources.Count);
        Assert.Contains(
            edgeFact.Provenance.Sources,
            record => record.Locator == "data/compliance/requirements/nis2-article-21-supply-chain.yaml");
        Assert.Contains(
            edgeFact.Provenance.Sources,
            record => record.Locator == "data/security/controls/adoptions/ca-artifact-supply-chain.yaml");
    }

    [Fact]
    public void TheNarrativeStatesEveryStepWithItsTypedEdgeAndTheObservedState()
    {
        var (graph, store) = LoadSharedWorld();

        var narrative = DomainNarratives.SupplyChainCompliance(graph, store);

        var texts = narrative.Statements.Select(statement => statement.Text).ToArray();

        // The chain reads as its own justification: each step names the
        // typed edge that connects it, as the narrative's phrase table
        // renders it.
        Assert.Contains(
            texts,
            text => text.StartsWith("\"NIS2 Article 21 — Supply Chain Security\" (requirement:nis2-article-21-supply-chain) is satisfied by \"Adoption of ARR-3-11 Secure Software Artifact Supply Chain\"", StringComparison.Ordinal));
        Assert.Contains(
            texts,
            text => text.StartsWith("\"Adoption of ARR-3-11 Secure Software Artifact Supply Chain\" (control-adoption:ca-artifact-supply-chain) is implemented by \"Signed Release Pipeline\"", StringComparison.Ordinal));
        Assert.Contains(
            texts,
            text => text.StartsWith("\"Signed Release Pipeline\" (implementation:signed-release-pipeline) is validated by \"Production artifact provenance validation\"", StringComparison.Ordinal));

        // Every observation step states the recorded result and the
        // validator that produced it; the recorded failure of the blocked
        // unsigned upload stays a failure.
        var failed = Assert.Single(texts, text => text.Contains("observation:obs-2026-08-06-001", StringComparison.Ordinal));
        Assert.Contains("recorded fail", failed);
        Assert.Contains("by validator provenance-prober@3.1.7", failed);
        Assert.DoesNotContain("surfaces", failed);

        // The two observations produced by outdated validators state the
        // store's current surface against the recorded pass — never smoothed
        // into current evidence.
        var stale = Assert.Single(texts, text => text.Contains("observation:obs-2026-08-05-001", StringComparison.Ordinal));
        Assert.Contains("The store currently surfaces stale (recorded pass).", stale);
        Assert.Contains(
            "The store currently surfaces stale (recorded pass).",
            Assert.Single(texts, text => text.Contains("observation:obs-2026-07-15-001", StringComparison.Ordinal)));

        // The verdict sentence closes the narrative as an established
        // conclusion over the compiled paths — nothing beyond them.
        Assert.EndsWith(
            "The supply chain assurance path from \"NIS2 Article 21 — Supply Chain Security\" (requirement:nis2-article-21-supply-chain) is established: 5 validated paths, 8 distinct steps, every hop a stored typed edge of the shared graph.",
            texts[^1]);
    }

    [Fact]
    public void TheDefaultRenderingIsDeterministicAndRequiresNoAi()
    {
        var (graph, store) = LoadSharedWorld();

        var first = NarrativeRendering.Render(DomainNarratives.SupplyChainCompliance(graph, store));
        var second = NarrativeRendering.Render(DomainNarratives.SupplyChainCompliance(graph, store));

        Assert.Equal(first, second);

        // The default renderer is an ordinary INarrativeRenderer
        // implementation: the same deterministic output, no model, no
        // configuration.
        DefaultNarrativeRenderer renderer = new();
        Assert.Equal(first, renderer.Render(DomainNarratives.SupplyChainCompliance(graph, store)));
    }

    [Fact]
    public void ARequirementWithoutAnAssurancePathCompilesToAnExplicitGapAndNoConclusion()
    {
        // A world whose requirement exists but is satisfied by nothing: the
        // grammar's verdict is an explicit no-valid-path, and the compiled
        // narrative states where the chain breaks instead of manufacturing
        // a conclusion.
        var graph = new TypedPropertyGraph(EdgeTypeRegistry.Slice);
        graph.AddNode(DomainViews.ComplianceSubjectId, Node("RegulatoryRequirement"));
        var result = CanonicalPathGrammars.SupplyChainAssurance(graph.Registry)
            .Evaluate(graph, DomainViews.ComplianceSubjectId);

        var narrative = NarrativeCompiler.Compile(result, graph);

        Assert.Equal(NarrativeVerdict.Gap, narrative.Verdict);
        Assert.Equal(0, narrative.PathCount);
        Assert.Empty(narrative.Steps);

        // The gap names the required step, the frontier node it is missing
        // from, and the node type the step had to reach.
        var gap = Assert.Single(narrative.Gaps);
        Assert.Equal("satisfied-by", gap.RequiredStepName);
        Assert.Equal("requirement:nis2-article-21-supply-chain", gap.FromNodeId);
        Assert.Equal("ControlAdoption", gap.RequiredNodeType);

        // The narrative states the break and draws no conclusion — no
        // sentence claims establishment, and nothing is inferred from the
        // absence of a path.
        Assert.Contains(
            narrative.Statements.Select(statement => statement.Text),
            text => text.Contains("has no stored 'satisfied-by' edge reaching any ControlAdoption node", StringComparison.Ordinal));
        Assert.DoesNotContain(
            narrative.Statements.Select(statement => statement.Text),
            text => text.Contains("is established", StringComparison.Ordinal));
        Assert.EndsWith(
            "The supply chain assurance path from requirement:nis2-article-21-supply-chain does not exist: no conclusion is drawn, and nothing is inferred from the absence of a path.",
            narrative.Statements[^1].Text);

        Golden.AssertMatches("narrative/gap.txt", NarrativeRendering.Render(narrative));
    }

    [Fact]
    public void TheAiRendererBoundaryReceivesOnlyTheCompiledStructure()
    {
        var (graph, store) = LoadSharedWorld();
        var narrative = DomainNarratives.SupplyChainCompliance(graph, store);

        // An AI renderer implements the same boundary the default renderer
        // implements: INarrativeRenderer.Render takes the compiled narrative
        // and nothing else — no graph, no store, no traversal engine — so a
        // renderer can rephrase the established sentences but has no surface
        // through which to add an edge, a fact, or a conclusion. Everything
        // a renderer may say is already in the compiled structure: the
        // verdict, the steps, and the facts with their provenance.
        var renderer = new RephrasingRenderer();
        var output = renderer.Render(narrative);

        Assert.Equal(
            string.Join('\n', narrative.Statements.Select(statement => statement.Text.ToUpperInvariant())),
            output);

        // The structure a renderer receives is immutable data: the verdict
        // and the gaps travel with it, so a gap narrative cannot be rendered
        // as a conclusion by reading what it was given.
        Assert.Equal(NarrativeVerdict.Established, narrative.Verdict);
        Assert.Empty(narrative.Gaps);
        Assert.All(narrative.Facts, fact => Assert.NotNull(fact.Provenance));
    }

    [Fact]
    public void TheVerbPhrasesPinTheDemonstratedEdgeNames()
    {
        Assert.Equal("is satisfied by", NarrativeCompiler.VerbPhrase("satisfied-by", EdgeDirection.Inverse));
        Assert.Equal("is implemented by", NarrativeCompiler.VerbPhrase("implemented-by", EdgeDirection.Forward));
        Assert.Equal("is validated by", NarrativeCompiler.VerbPhrase("validated-by", EdgeDirection.Inverse));
        Assert.Equal("produces", NarrativeCompiler.VerbPhrase("produces", EdgeDirection.Forward));

        // Names outside the phrase table fall back to the name's words,
        // following the traversed direction.
        Assert.Equal("hypothetical edge", NarrativeCompiler.VerbPhrase("hypothetical-edge", EdgeDirection.Forward));
        Assert.Equal("is hypothetical edge by", NarrativeCompiler.VerbPhrase("hypothetical-edge", EdgeDirection.Inverse));
    }

    [Fact]
    public void AValidatedPathCompilesDirectlyIntoAnEstablishedNarrative()
    {
        var graph = LoadSharedGraph();

        // The artifact-to-source chain as a bare validated path: the
        // single-path compiler entry point, with no grammar attached.
        var path = Assert.Single(CanonicalTraversals.ArtifactAttestedDerivation(graph).Execute());

        var narrative = NarrativeCompiler.Compile(path);

        Assert.Equal(NarrativeVerdict.Established, narrative.Verdict);
        Assert.Null(narrative.GrammarName);
        Assert.Equal(1, narrative.PathCount);
        Assert.Equal(3, narrative.Steps.Count);
        Assert.Equal(
            [
                "artifact:customer-agent-2.8.4",
                "build-run:84125",
                "commit:7a94f3e8b2c1d5f6a7b8c9d0e1f2a3b4c5d6e7f8",
                "repository:customer-agent",
            ],
            [narrative.Steps[0].FromNodeId, .. narrative.Steps.Select(step => step.ToNodeId)]);
        Assert.EndsWith(
            "is established: 1 validated path, 3 distinct steps, every hop a stored typed edge of the shared graph.",
            narrative.Statements[^1].Text);
    }

    /// <summary>An example downstream renderer: rephrases the compiled sentences, invents nothing.</summary>
    private sealed class RephrasingRenderer : INarrativeRenderer
    {
        public string Render(CompiledNarrative narrative) =>
            string.Join('\n', narrative.Statements.Select(statement => statement.Text.ToUpperInvariant()));
    }

    private static TypedPropertyGraph LoadSharedGraph() => LoadSharedWorld().Graph;

    private static (TypedPropertyGraph Graph, ObservationStore Store) LoadSharedWorld()
    {
        // The shared world all narratives compile from: the declared records
        // under data/** merged with the observed facts the fixture adapters
        // normalize from fixtures/**, and the observation store fed the
        // recorded history from the same records.
        var graph = new TypedPropertyGraph(EdgeTypeRegistry.Slice);
        var world = RepositoryDataLoader.Load(TestWorld.RepositoryRoot);
        DeclaredRecordsLoader.ApplyAll(graph, world.Records);
        FixtureAdapterRegistry.MockWorld.ApplyAll(graph, world.Fixtures);

        var store = new ObservationStore();
        HistoricalObservationLoader.ApplyAll(store, world.Records);
        return (graph, store);
    }

    private static NodeType Node(string name) =>
        EdgeTypeRegistry.Slice.NodeTypes.Single(type => type.Name == name);
}
