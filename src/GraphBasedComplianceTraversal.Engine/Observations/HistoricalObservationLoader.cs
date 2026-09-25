using System.Globalization;
using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Provenance;
using GraphBasedComplianceTraversal.Engine.State;

namespace GraphBasedComplianceTraversal.Engine.Observations;

/// <summary>
/// Loads the repository's recorded observation history — the documents under
/// <c>data/**</c> whose root carries an <c>observations</c> list, for example
/// <c>data/security/observations/provenance-observations.yaml</c> — into an
/// <see cref="ObservationStore"/> as ingested <see cref="HistoricalObservation"/>
/// entries. Every entry keeps its recorded result, subject, timestamp,
/// validator version, details, and declaration provenance naming its
/// document. An entry whose shape or field values are malformed is rejected
/// with an <see cref="ObservationRecordException"/> naming the document's
/// path — recorded history is never silently dropped or repaired.
/// </summary>
public static class HistoricalObservationLoader
{
    /// <summary>
    /// Loads every observations-history document among the records into the
    /// store, in document order. A document whose root carries an
    /// <c>observations</c> key is an observations history; a document without
    /// one is not this loader's concern and is left alone.
    /// </summary>
    public static void ApplyAll(ObservationStore store, IReadOnlyList<RepositoryDocument> records)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(records);
        foreach (var record in records)
        {
            ApplyRecord(store, record);
        }
    }

    private static void ApplyRecord(ObservationStore store, RepositoryDocument record)
    {
        var root = record.Content as JsonObject;
        if (root is null || !root.TryGetPropertyValue("observations", out var observations))
        {
            return;
        }

        var entries = observations as JsonArray
            ?? throw Invalid(record, "the 'observations' field must be a list.");
        foreach (var element in entries)
        {
            var entry = element as JsonObject
                ?? throw Invalid(record, "every entry of 'observations' must be an object.");
            store.Ingest(Parse(record, entry));
        }
    }

    private static HistoricalObservation Parse(RepositoryDocument record, JsonObject entry)
    {
        var subject = RequireObject(record, entry, "subject");
        return new HistoricalObservation(
            RequireString(record, entry, "id"),
            RequireString(record, entry, "assertion"),
            RequireString(record, subject, "artifact"),
            RequireResult(record, RequireString(record, entry, "result")),
            RequireTimestamp(record, RequireString(record, entry, "observed_at")),
            RequireValidatorVersion(record, RequireString(record, entry, "validator_version")),
            RequireString(record, entry, "details"),
            new FactProvenance(new ProvenanceRecord(SourceKind.Declaration, record.RelativePath)),
            OptionalString(subject, "digest"),
            OptionalString(entry, "related_incident"));
    }

    private static ValidationState RequireResult(RepositoryDocument record, string result)
    {
        if (result.Equals("pass", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationState.Pass;
        }

        if (result.Equals("fail", StringComparison.OrdinalIgnoreCase))
        {
            return ValidationState.Fail;
        }

        throw Invalid(record, $"field 'result' must be 'pass' or 'fail', not '{result}'.");
    }

    private static DateTimeOffset RequireTimestamp(RepositoryDocument record, string observedAt) =>
        DateTimeOffset.TryParse(observedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp)
            ? timestamp
            : throw Invalid(record, $"field 'observed_at' must be an ISO-8601 timestamp, not '{observedAt}'.");

    private static string RequireValidatorVersion(RepositoryDocument record, string validatorVersion) =>
        ValidatorVersion.IsValid(validatorVersion)
            ? validatorVersion
            : throw Invalid(
                record,
                $"field 'validator_version' must name a validator and version as 'name@major.minor.patch', not '{validatorVersion}'.");

    private static string RequireString(RepositoryDocument record, JsonObject obj, string field) =>
        OptionalString(obj, field)
        ?? throw Invalid(record, $"field '{field}' must be a string.");

    private static JsonObject RequireObject(RepositoryDocument record, JsonObject obj, string field) =>
        obj[field] as JsonObject
        ?? throw Invalid(record, $"field '{field}' must be an object.");

    private static string? OptionalString(JsonObject obj, string field) =>
        obj.TryGetPropertyValue(field, out var value)
        && value is JsonValue jsonValue
        && jsonValue.TryGetValue<string>(out var text)
            ? text
            : null;

    private static ObservationRecordException Invalid(RepositoryDocument record, string message) =>
        new(record.RelativePath, message);
}
