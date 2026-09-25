using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Adapters;

/// <summary>
/// Normalizes a mocked GitHub repository response into the repository facts
/// the code host records. The subject's stable id is derived from the
/// repository name in the response itself (<c>repository:{name}</c>), which is
/// how a live connector would key the repository it fetched.
/// </summary>
public sealed class GithubRepositoryAdapter : ExternalSourceAdapter
{
    /// <inheritdoc/>
    public override string Name => "github.repository";

    /// <inheritdoc/>
    public override SourceKind SourceKind => SourceKind.Github;

    /// <inheritdoc/>
    public override bool CanNormalize(RepositoryDocument document) =>
        document.Content is JsonObject root
        && root["name"] is JsonValue
        && root["full_name"] is JsonValue
        && root["default_branch"] is JsonValue;

    /// <inheritdoc/>
    public override NormalizedState Normalize(RepositoryDocument document, IReadOnlyList<RepositoryDocument> availableDocuments)
    {
        var root = RequireRoot(document);
        var name = RequireString(document, root, "name");
        var fullName = RequireString(document, root, "full_name");
        if (!fullName.EndsWith("/" + name, StringComparison.Ordinal))
        {
            throw Invalid(document, $"full_name '{fullName}' is not consistent with name '{name}'.");
        }

        var owner = RequireObject(document, root, "owner");

        var provenance = new FactProvenance(Record(document));
        var properties = new List<NormalizedProperty>
        {
            new("name", JsonValue.Create(name), provenance),
            new("full_name", JsonValue.Create(fullName), provenance),
            new("url", JsonValue.Create(RequireString(document, root, "html_url")), provenance),
            new("description", JsonValue.Create(RequireString(document, root, "description")), provenance),
            new("default_branch", JsonValue.Create(RequireString(document, root, "default_branch")), provenance),
            new("private", JsonValue.Create(RequireBoolean(document, root, "private")), provenance),
            new("archived", JsonValue.Create(RequireBoolean(document, root, "archived")), provenance),
            new("visibility", JsonValue.Create(RequireString(document, root, "visibility")), provenance),
            new("owner", JsonValue.Create(RequireString(document, owner, "login")), provenance),
        };

        // The world's records write the language in lower case; normalize the
        // API's capitalization so the observed value addresses the same
        // vocabulary.
        if (root.TryGetPropertyValue("language", out var language) && language is JsonValue languageValue)
        {
            properties.Add(new NormalizedProperty(
                "language",
                JsonValue.Create(languageValue.GetValue<string>().ToLowerInvariant()),
                provenance));
        }

        if (root.TryGetPropertyValue("topics", out var topics) && topics is JsonArray topicArray)
        {
            properties.Add(new NormalizedProperty("topics", topicArray.DeepClone(), provenance));
        }

        return new NormalizedState().AddNode(new NormalizedNode(
            $"repository:{name}",
            NodeType("SourceRepository"),
            properties,
            provenance));
    }
}
