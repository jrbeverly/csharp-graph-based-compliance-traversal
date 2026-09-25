using GraphBasedComplianceTraversal.Engine.Assertions;
using GraphBasedComplianceTraversal.Engine.Provenance;
using GraphBasedComplianceTraversal.Engine.State;

namespace GraphBasedComplianceTraversal.Engine.Observations;

/// <summary>
/// The store that holds the observations of every assertion — the recorded
/// history ingested from the repository (see
/// <see cref="HistoricalObservationLoader"/>) and the observations freshly
/// produced by evaluation — keyed by assertion and subject, and queryable as
/// one chronological timeline per assertion. The store never discards
/// anything and never rewrites history: an ingested observation keeps the
/// <see cref="ValidationState"/> it was recorded with, and an observation
/// produced by a validator version older than the assertion's current
/// validator version is surfaced <see cref="ValidationState.Stale"/> while
/// its recorded state stays visible next to it. The current validator
/// version of an assertion is the newest validator version recorded across
/// its observations, and staleness is derived from that comparison — the
/// store never reads the clock, so its answers are deterministic.
/// </summary>
public sealed class ObservationStore
{
    private readonly Dictionary<string, List<Entry>> _byAssertion = new(StringComparer.Ordinal);
    private readonly Dictionary<(string AssertionId, string SubjectId), List<Entry>> _byAssertionAndSubject = [];
    private readonly HashSet<string> _observationIds = new(StringComparer.Ordinal);
    private long _nextSequence;

    /// <summary>
    /// Records one freshly evaluated observation. The entry keeps the state,
    /// timestamp, provenance, and reason the evaluation produced; it carries
    /// no observation id and no validator version, so the staleness rule —
    /// which compares validator versions — never flags it: an evaluation by
    /// the engine is current by construction.
    /// </summary>
    public void Record(AssertionObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.AssertionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.SubjectId);
        ArgumentNullException.ThrowIfNull(observation.Provenance);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.Reason);

        Add(
            observation.AssertionId,
            new Entry(
                NextSequence(),
                observation.SubjectId,
                observation.State,
                observation.EvaluatedAt,
                observation.Provenance,
                observation.Reason,
                null,
                null,
                null,
                null));
    }

    /// <summary>Records freshly evaluated observations, in the order given.</summary>
    public void Record(IEnumerable<AssertionObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        foreach (var observation in observations)
        {
            Record(observation);
        }
    }

    /// <summary>
    /// Ingests one entry of the repository's recorded history. The entry
    /// keeps its observation id, recorded result, timestamp, validator
    /// version, details, and provenance. A repeated observation id is
    /// rejected — recorded history names its entries, and two entries must
    /// not share a name.
    /// </summary>
    public void Ingest(HistoricalObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.AssertionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.SubjectId);
        ArgumentNullException.ThrowIfNull(observation.Provenance);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.Details);
        ArgumentException.ThrowIfNullOrWhiteSpace(observation.ValidatorVersion);

        if (!ValidatorVersion.IsValid(observation.ValidatorVersion))
        {
            throw new ArgumentException(
                $"Validator version '{observation.ValidatorVersion}' must name a validator and version as 'name@major.minor.patch'.",
                nameof(observation));
        }

        if (!_observationIds.Add(observation.Id))
        {
            throw new ArgumentException($"Observation id '{observation.Id}' is ingested more than once.", nameof(observation));
        }

        Add(
            observation.AssertionId,
            new Entry(
                NextSequence(),
                observation.SubjectId,
                observation.Result,
                observation.ObservedAt,
                observation.Provenance,
                observation.Details,
                observation.Id,
                observation.ValidatorVersion,
                observation.SubjectDigest,
                observation.RelatedIncident));
    }

    /// <summary>
    /// The assertion's observations across all its subjects, as one
    /// chronological timeline ordered by observation time (ties broken by
    /// ingestion order), each surfaced with the staleness the store derives.
    /// An assertion with no observations answers an empty timeline.
    /// </summary>
    public IReadOnlyList<StoredObservation> Timeline(string assertionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assertionId);
        return _byAssertion.TryGetValue(assertionId, out var entries)
            ? Project(entries, assertionId)
            : [];
    }

    /// <summary>
    /// The assertion's observations for one subject, in the same
    /// chronological order as <see cref="Timeline(string)"/>. A subject with
    /// no observations answers an empty timeline.
    /// </summary>
    public IReadOnlyList<StoredObservation> Timeline(string assertionId, string subjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assertionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        return _byAssertionAndSubject.TryGetValue((assertionId, subjectId), out var entries)
            ? Project(entries, assertionId)
            : [];
    }

    /// <summary>
    /// The assertion's current validator version: the newest validator
    /// version recorded across the assertion's observations, or <c>null</c>
    /// when the assertion has no observations or none of them carries a
    /// validator version (freshly evaluated observations do not). This is
    /// the baseline every observation's staleness is decided against.
    /// </summary>
    public string? CurrentValidatorVersion(string assertionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assertionId);
        if (!_byAssertion.TryGetValue(assertionId, out var entries))
        {
            return null;
        }

        string? current = null;
        foreach (var entry in entries)
        {
            if (entry.ValidatorVersion is { } version
                && (current is null || ValidatorVersion.Compare(version, current) > 0))
            {
                current = version;
            }
        }

        return current;
    }

    private StoredObservation[] Project(List<Entry> entries, string assertionId)
    {
        var currentValidatorVersion = CurrentValidatorVersion(assertionId);
        return entries
            .OrderBy(entry => entry.ObservedAt)
            .ThenBy(entry => entry.Sequence)
            .Select(entry => new StoredObservation(
                assertionId,
                entry.SubjectId,
                entry.RecordedState,
                entry.ObservedAt,
                entry.Provenance,
                entry.Reason,
                IsStale(entry.ValidatorVersion, currentValidatorVersion),
                entry.ObservationId,
                entry.ValidatorVersion,
                entry.SubjectDigest,
                entry.RelatedIncident))
            .ToArray();
    }

    private static bool IsStale(string? validatorVersion, string? currentValidatorVersion) =>
        validatorVersion is not null
        && currentValidatorVersion is not null
        && ValidatorVersion.NameOf(validatorVersion) == ValidatorVersion.NameOf(currentValidatorVersion)
        && ValidatorVersion.Compare(validatorVersion, currentValidatorVersion) < 0;

    private void Add(string assertionId, Entry entry)
    {
        AddToIndex(_byAssertion, assertionId, entry);
        AddToIndex(_byAssertionAndSubject, (assertionId, entry.SubjectId), entry);
    }

    private long NextSequence() => _nextSequence++;

    private static void AddToIndex<TKey>(Dictionary<TKey, List<Entry>> index, TKey key, Entry entry)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out var entries))
        {
            index[key] = entries = [];
        }

        entries.Add(entry);
    }

    private sealed record Entry(
        long Sequence,
        string SubjectId,
        ValidationState RecordedState,
        DateTimeOffset ObservedAt,
        FactProvenance Provenance,
        string Reason,
        string? ObservationId,
        string? ValidatorVersion,
        string? SubjectDigest,
        string? RelatedIncident);
}
