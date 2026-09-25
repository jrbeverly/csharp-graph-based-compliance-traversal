using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Adapters;

/// <summary>
/// Normalizes a mocked GitHub commit response into the commit facts the code
/// host records. The subject's stable id is the commit's own SHA
/// (<c>commit:{sha}</c>) — the identifier every other source refers to the
/// commit by. The response does not name its repository (the real API takes
/// it from the request path), so the commit-to-repository edge is established
/// by the sources that do name both — the CI workflow-run record and the
/// attestation.
/// </summary>
public sealed class GithubCommitAdapter : ExternalSourceAdapter
{
    /// <inheritdoc/>
    public override string Name => "github.commit";

    /// <inheritdoc/>
    public override SourceKind SourceKind => SourceKind.Github;

    /// <inheritdoc/>
    public override bool CanNormalize(RepositoryDocument document) =>
        document.Content is JsonObject root
        && root["sha"] is JsonValue
        && root["commit"] is JsonObject commit
        && commit["tree"] is JsonObject;

    /// <inheritdoc/>
    public override NormalizedState Normalize(RepositoryDocument document, IReadOnlyList<RepositoryDocument> availableDocuments)
    {
        var root = RequireRoot(document);
        var sha = RequireString(document, root, "sha");
        RequireHexSha(document, sha, "sha");

        var commit = RequireObject(document, root, "commit");
        var committer = RequireObject(document, commit, "committer");
        var committedAt = RequireTimestamp(document, committer, "date");

        var provenance = new FactProvenance(Record(document, committedAt));
        var properties = new List<NormalizedProperty>
        {
            new("sha", JsonValue.Create(sha), provenance),
            new("message", JsonValue.Create(RequireString(document, commit, "message")), provenance),
            new("committed_at", JsonValue.Create(RequireString(document, committer, "date")), provenance),
        };
        if (commit.TryGetPropertyValue("author", out var author) && author is not null)
        {
            properties.Add(new NormalizedProperty("author", author.DeepClone(), provenance));
        }

        properties.Add(new NormalizedProperty("committer", committer.DeepClone(), provenance));
        if (root.TryGetPropertyValue("parents", out var parents) && parents is JsonArray parentArray)
        {
            properties.Add(new NormalizedProperty("parents", parentArray.DeepClone(), provenance));
        }

        return new NormalizedState().AddNode(new NormalizedNode(
            $"commit:{sha}",
            NodeType("Commit"),
            properties,
            provenance));
    }
}
