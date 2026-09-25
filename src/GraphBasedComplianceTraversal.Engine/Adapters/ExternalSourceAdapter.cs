using System.Globalization;
using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Adapters;

/// <summary>
/// Base class for concrete adapters: carries the provenance kind and provides
/// the small parsing helpers, so each adapter states only which facts its
/// response shape normalizes to. All helpers throw
/// <see cref="AdapterException"/> naming the response's locator, so a
/// malformed or incomplete response fails loudly rather than emitting a
/// partial fact set.
/// </summary>
public abstract class ExternalSourceAdapter : IExternalSourceAdapter
{
    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public abstract SourceKind SourceKind { get; }

    /// <inheritdoc/>
    public abstract bool CanNormalize(RepositoryDocument document);

    /// <inheritdoc/>
    public abstract NormalizedState Normalize(RepositoryDocument document, IReadOnlyList<RepositoryDocument> availableDocuments);

    /// <summary>
    /// The provenance record for facts read from <paramref name="document"/>,
    /// stamped with this adapter's source kind and the document's locator.
    /// The timestamp and version are recorded only where the source provides
    /// them.
    /// </summary>
    protected ProvenanceRecord Record(RepositoryDocument document, DateTimeOffset? timestamp = null, string? version = null) =>
        new(SourceKind, document.RelativePath, timestamp, version);

    /// <summary>The declared node type with the given name, canonicalized to the ontology slice.</summary>
    protected static NodeType NodeType(string name) =>
        EdgeTypeRegistry.Slice.NodeTypes.Single(type => type.Name == name);

    protected static JsonObject RequireRoot(RepositoryDocument document) =>
        document.Content is JsonObject root
            ? root
            : throw new AdapterException(document.RelativePath, "the response must be a JSON object.");

    protected static JsonObject RequireObject(RepositoryDocument document, JsonObject parent, string field)
    {
        if (parent.TryGetPropertyValue(field, out var value) && value is JsonObject obj)
        {
            return obj;
        }

        throw new AdapterException(document.RelativePath, $"Field '{field}' must be an object.");
    }

    protected static JsonArray RequireArray(RepositoryDocument document, JsonObject parent, string field)
    {
        if (parent.TryGetPropertyValue(field, out var value) && value is JsonArray array)
        {
            return array;
        }

        throw new AdapterException(document.RelativePath, $"Field '{field}' must be an array.");
    }

    protected static JsonObject RequireElementObject(RepositoryDocument document, JsonNode? element, string path)
    {
        if (element is JsonObject obj)
        {
            return obj;
        }

        throw new AdapterException(document.RelativePath, $"Every element of '{path}' must be an object.");
    }

    protected static string RequireString(RepositoryDocument document, JsonObject parent, string field)
    {
        if (parent.TryGetPropertyValue(field, out var value) && value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text))
        {
            return text;
        }

        throw new AdapterException(document.RelativePath, $"Field '{field}' must be a string.");
    }

    protected static bool RequireBoolean(RepositoryDocument document, JsonObject parent, string field)
    {
        if (parent.TryGetPropertyValue(field, out var value) && value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var boolean))
        {
            return boolean;
        }

        throw new AdapterException(document.RelativePath, $"Field '{field}' must be a boolean.");
    }

    protected static long RequireNumber(RepositoryDocument document, JsonObject parent, string field)
    {
        if (parent.TryGetPropertyValue(field, out var value) && value is JsonValue jsonValue && jsonValue.TryGetValue<long>(out var number))
        {
            return number;
        }

        throw new AdapterException(document.RelativePath, $"Field '{field}' must be a number.");
    }

    protected static DateTimeOffset RequireTimestamp(RepositoryDocument document, JsonObject parent, string field)
    {
        var text = RequireString(document, parent, field);
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp))
        {
            return timestamp;
        }

        throw new AdapterException(document.RelativePath, $"Field '{field}' must be an ISO-8601 timestamp, got '{text}'.");
    }

    protected static AdapterException Invalid(RepositoryDocument document, string message) =>
        new(document.RelativePath, message);

    /// <summary>Requires a hexadecimal commit SHA of at least 7 characters.</summary>
    protected static void RequireHexSha(RepositoryDocument document, string sha, string field)
    {
        if (sha.Length < 7 || !sha.All(Uri.IsHexDigit))
        {
            throw Invalid(document, $"field '{field}' must be a hexadecimal commit SHA of at least 7 characters, got '{sha}'.");
        }
    }

    /// <summary>Extracts the repository name from an <c>owner/name</c> pair such as <c>nexusdefend/customer-agent</c>.</summary>
    protected static string RepositoryNameFromFullName(RepositoryDocument document, string fullName)
    {
        var separator = fullName.LastIndexOf('/');
        if (separator <= 0 || separator == fullName.Length - 1)
        {
            throw Invalid(document, $"repository name '{fullName}' is not an owner/name pair.");
        }

        return fullName[(separator + 1)..];
    }
}
