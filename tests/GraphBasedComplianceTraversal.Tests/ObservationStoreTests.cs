using System.Globalization;
using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Assertions;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ingestion;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Observations;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;
using GraphBasedComplianceTraversal.Engine.State;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Pins the observation-store milestone: the repository's recorded
/// observation history is ingested into the store keyed by assertion and
/// subject, the full PASS/FAIL history — including the blocked unsigned
/// upload — is returned as one chronological timeline, an observation
/// produced by an outdated validator version is surfaced STALE while its
/// recorded state stays queryable, and freshly evaluated observations
/// coexist with the ingested history in the same timeline. The scenarios
/// run against the repository's own data world; malformed history entries
/// are rejected loudly, naming their document.
/// </summary>
public sealed class ObservationStoreTests
{
    private const string ProvenanceAssertion = "assertion:prod-artifact-provenance";
    private const string ObservationsLocator = "data/security/observations/provenance-observations.yaml";

    private static readonly DateTimeOffset EvaluatedAt =
        DateTimeOffset.Parse("2026-08-12T09:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public void TheStoreReturnsTheFullPassFailHistoryInChronologicalOrder()
    {
        var store = LoadedStore();

        var timeline = store.Timeline(ProvenanceAssertion);

        // Five recorded observations, chronologically ordered, preserving
        // every recorded state — including the FAIL of the blocked unsigned
        // upload. The two oldest entries were produced by outdated validator
        // versions, so they are surfaced stale; their recorded passes stay
        // visible.
        Assert.Equal(
            [
                ("artifact:customer-agent-2.8.1", ValidationState.Pass),
                ("artifact:customer-agent-2.8.2", ValidationState.Pass),
                ("artifact:customer-agent-2.8.4-unsigned", ValidationState.Fail),
                ("artifact:customer-agent-2.8.3", ValidationState.Pass),
                ("artifact:customer-agent-2.8.4", ValidationState.Pass),
            ],
            timeline.Select(observation => (observation.SubjectId, observation.RecordedState)).ToArray());
        Assert.Equal(
            [true, true, false, false, false],
            timeline.Select(observation => observation.IsStale).ToArray());

        var failure = Assert.Single(timeline, observation => observation.ObservationId == "observation:obs-2026-08-06-001");
        Assert.Equal("artifact:customer-agent-2.8.4-unsigned", failure.SubjectId);
        Assert.Equal(ValidationState.Fail, failure.State);
        Assert.False(failure.IsStale);
        Assert.Equal("provenance-prober@3.1.7", failure.ValidatorVersion);
        Assert.Contains("INC-2026-041", failure.RelatedIncident, StringComparison.Ordinal);
        Assert.Contains("blocked", failure.Reason, StringComparison.OrdinalIgnoreCase);

        // Every ingested entry keeps declaration provenance naming the
        // history document it came from.
        Assert.All(timeline, observation =>
        {
            var source = Assert.Single(observation.Provenance.Sources);
            Assert.Equal(SourceKind.Declaration, source.Kind);
            Assert.Equal(ObservationsLocator, source.Locator);
        });
    }

    [Fact]
    public void AnObservationFromAnOutdatedValidatorVersionIsStaleButStillQueryable()
    {
        var store = LoadedStore();

        // The 2.8.1 observation was produced by provenance-prober@3.1.5,
        // behind the current validator (3.1.7): surfaced STALE, with its
        // recorded pass intact and the entry fully queryable.
        var stale = Assert.Single(store.Timeline(ProvenanceAssertion, "artifact:customer-agent-2.8.1"));
        Assert.Equal("observation:obs-2026-07-15-001", stale.ObservationId);
        Assert.True(stale.IsStale);
        Assert.Equal(ValidationState.Stale, stale.State);
        Assert.Equal(ValidationState.Pass, stale.RecordedState);
        Assert.Equal("provenance-prober@3.1.5", stale.ValidatorVersion);
        Assert.NotEmpty(stale.Reason);

        // The same rule surfaces the 3.1.6 observation as stale too: one
        // version behind the current validator.
        var oneBehind = Assert.Single(store.Timeline(ProvenanceAssertion, "artifact:customer-agent-2.8.2"));
        Assert.True(oneBehind.IsStale);
        Assert.Equal(ValidationState.Stale, oneBehind.State);
        Assert.Equal(ValidationState.Pass, oneBehind.RecordedState);

        // The observations produced by the current validator are not stale.
        var current = Assert.Single(store.Timeline(ProvenanceAssertion, "artifact:customer-agent-2.8.4"));
        Assert.False(current.IsStale);
        Assert.Equal(ValidationState.Pass, current.State);
    }

    [Fact]
    public void EvaluatedAndIngestedObservationsShareOneChronologicalTimeline()
    {
        var store = LoadedStore();
        var graph = LoadDeclaredGraph();
        store.Record(AssertionRegistry.Slice.EvaluateAll(graph, EvaluatedAt));

        // The provenance assertion's timeline holds its five ingested
        // history entries followed by the freshly evaluated observation of
        // the declared production artifact, which carries the latest time.
        var timeline = store.Timeline(ProvenanceAssertion);
        Assert.Equal(6, timeline.Count);
        Assert.All(timeline.Take(5), observation => Assert.NotNull(observation.ObservationId));

        var evaluated = timeline[5];
        Assert.Null(evaluated.ObservationId);
        Assert.Equal("artifact:customer-agent-2.8.4", evaluated.SubjectId);
        Assert.Equal(ValidationState.Pass, evaluated.RecordedState);
        Assert.Equal(EvaluatedAt, evaluated.ObservedAt);
        Assert.Contains("signing", evaluated.Reason, StringComparison.Ordinal);
        Assert.Null(evaluated.ValidatorVersion);
        Assert.False(evaluated.IsStale);

        // The other assertions' timelines hold only their evaluated
        // observations; the recorded history leaves them untouched.
        Assert.Equal(
            ["aws:cloudfront:customer-downloads", "aws:s3:prod-release-artifacts"],
            store.Timeline("assertion:production-resource-owner")
                .Select(observation => observation.SubjectId)
                .ToArray());
    }

    [Fact]
    public void TheCurrentValidatorVersionIsTheNewestVersionRecordedForTheAssertion()
    {
        var store = LoadedStore();

        // The history records 3.1.5, 3.1.6, and 3.1.7; the newest is the
        // assertion's current validator.
        Assert.Equal("provenance-prober@3.1.7", store.CurrentValidatorVersion(ProvenanceAssertion));
    }

    [Fact]
    public void AnAssertionWithNoObservationsAnswersAnEmptyTimelineAndNoCurrentValidator()
    {
        var store = new ObservationStore();

        Assert.Empty(store.Timeline("assertion:production-resource-owner"));
        Assert.Empty(store.Timeline("assertion:production-resource-owner", "aws:s3:prod-release-artifacts"));
        Assert.Null(store.CurrentValidatorVersion("assertion:production-resource-owner"));
    }

    [Fact]
    public void AnEntryWithAnUnparseableObservedAtIsRejectedWithItsPath()
    {
        var record = ObservationsDocument(
            "data/security/observations/broken-time.yaml",
            """
            {"observations":[{"id":"observation:broken-time","assertion":"assertion:prod-artifact-provenance","subject":{"artifact":"artifact:x"},"result":"pass","observed_at":"yesterday","validator_version":"provenance-prober@3.1.7","details":"d"}]}
            """);

        var exception = Assert.Throws<ObservationRecordException>(() =>
            HistoricalObservationLoader.ApplyAll(new ObservationStore(), [record]));

        Assert.Equal("data/security/observations/broken-time.yaml", exception.Path);
        Assert.Contains("'observed_at'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryWithAMalformedValidatorVersionIsRejectedWithItsPath()
    {
        var record = ObservationsDocument(
            "data/security/observations/broken-version.yaml",
            """
            {"observations":[{"id":"observation:broken-version","assertion":"assertion:prod-artifact-provenance","subject":{"artifact":"artifact:x"},"result":"pass","observed_at":"2026-08-01T00:00:00Z","validator_version":"3.1.5","details":"d"}]}
            """);

        var exception = Assert.Throws<ObservationRecordException>(() =>
            HistoricalObservationLoader.ApplyAll(new ObservationStore(), [record]));

        Assert.Equal("data/security/observations/broken-version.yaml", exception.Path);
        Assert.Contains("'validator_version'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryWithAResultOtherThanPassOrFailIsRejected()
    {
        var record = ObservationsDocument(
            "data/security/observations/broken-result.yaml",
            """
            {"observations":[{"id":"observation:broken-result","assertion":"assertion:prod-artifact-provenance","subject":{"artifact":"artifact:x"},"result":"maybe","observed_at":"2026-08-01T00:00:00Z","validator_version":"provenance-prober@3.1.7","details":"d"}]}
            """);

        var exception = Assert.Throws<ObservationRecordException>(() =>
            HistoricalObservationLoader.ApplyAll(new ObservationStore(), [record]));

        Assert.Contains("'result' must be 'pass' or 'fail'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEntryWhoseSubjectNamesNoArtifactIsRejected()
    {
        var record = ObservationsDocument(
            "data/security/observations/broken-subject.yaml",
            """
            {"observations":[{"id":"observation:broken-subject","assertion":"assertion:prod-artifact-provenance","subject":{"digest":"sha256:abc"},"result":"pass","observed_at":"2026-08-01T00:00:00Z","validator_version":"provenance-prober@3.1.7","details":"d"}]}
            """);

        var exception = Assert.Throws<ObservationRecordException>(() =>
            HistoricalObservationLoader.ApplyAll(new ObservationStore(), [record]));

        Assert.Contains("'artifact'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondEntryWithTheSameObservationIdIsRejected()
    {
        var record = ObservationsDocument(
            "data/security/observations/duplicate.yaml",
            """
            {"observations":[
              {"id":"observation:same","assertion":"assertion:prod-artifact-provenance","subject":{"artifact":"artifact:a"},"result":"pass","observed_at":"2026-08-01T00:00:00Z","validator_version":"provenance-prober@3.1.7","details":"d"},
              {"id":"observation:same","assertion":"assertion:prod-artifact-provenance","subject":{"artifact":"artifact:b"},"result":"fail","observed_at":"2026-08-02T00:00:00Z","validator_version":"provenance-prober@3.1.7","details":"d"}]}
            """);

        var exception = Assert.Throws<ArgumentException>(() =>
            HistoricalObservationLoader.ApplyAll(new ObservationStore(), [record]));

        Assert.Contains("ingested more than once", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IngestingAnObservationWhoseValidatorVersionIsMalformedIsRejected()
    {
        var store = new ObservationStore();
        var observation = new HistoricalObservation(
            "observation:malformed",
            ProvenanceAssertion,
            "artifact:x",
            ValidationState.Pass,
            DateTimeOffset.Parse("2026-08-01T00:00:00Z", CultureInfo.InvariantCulture),
            "nonsense",
            "details",
            new FactProvenance(new ProvenanceRecord(SourceKind.Declaration, "data/x.yaml")),
            null,
            null);

        var exception = Assert.Throws<ArgumentException>(() => store.Ingest(observation));

        Assert.Contains("Validator version", exception.Message, StringComparison.Ordinal);
    }

    private static ObservationStore LoadedStore()
    {
        var store = new ObservationStore();
        HistoricalObservationLoader.ApplyAll(store, RepositoryDataLoader.Load(TestWorld.RepositoryRoot).Records);
        return store;
    }

    private static TypedPropertyGraph LoadDeclaredGraph()
    {
        var graph = new TypedPropertyGraph(EdgeTypeRegistry.Slice);
        DeclaredRecordsLoader.ApplyAll(graph, RepositoryDataLoader.Load(TestWorld.RepositoryRoot).Records);
        return graph;
    }

    private static RepositoryDocument ObservationsDocument(string relativePath, string json) =>
        new(relativePath, RepositoryDocumentFormat.Yaml, null, JsonNode.Parse(json)!);
}
