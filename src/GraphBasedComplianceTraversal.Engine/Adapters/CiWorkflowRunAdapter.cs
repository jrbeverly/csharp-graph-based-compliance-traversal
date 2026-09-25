using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Adapters;

/// <summary>
/// Normalizes a mocked CI workflow-run response into the build-run facts the
/// CI system records: the run itself (<c>build-run:{id}</c>), and the edges
/// tying the run to its head commit and that commit to its repository. The
/// run's repository is derived from the response's <c>repository.full_name</c>;
/// the commit and repository nodes themselves are emitted by the GitHub
/// adapters, and remain unresolved references until those states are applied.
/// </summary>
public sealed class CiWorkflowRunAdapter : ExternalSourceAdapter
{
    /// <inheritdoc/>
    public override string Name => "ci.workflow-run";

    /// <inheritdoc/>
    public override SourceKind SourceKind => SourceKind.Ci;

    /// <inheritdoc/>
    public override bool CanNormalize(RepositoryDocument document) =>
        document.Content is JsonObject root
        && root["workflow_id"] is JsonValue
        && root["head_sha"] is JsonValue
        && root["run_number"] is JsonValue;

    /// <inheritdoc/>
    public override NormalizedState Normalize(RepositoryDocument document, IReadOnlyList<RepositoryDocument> availableDocuments)
    {
        var root = RequireRoot(document);
        var runId = RequireNumber(document, root, "id");
        var headSha = RequireString(document, root, "head_sha");
        RequireHexSha(document, headSha, "head_sha");

        var repository = RequireObject(document, root, "repository");
        var repositoryName = RepositoryNameFromFullName(document, RequireString(document, repository, "full_name"));

        var createdAt = RequireTimestamp(document, root, "created_at");
        var provenance = new FactProvenance(Record(document, createdAt));
        var properties = new List<NormalizedProperty>
        {
            new("name", JsonValue.Create(RequireString(document, root, "name")), provenance),
            new("head_branch", JsonValue.Create(RequireString(document, root, "head_branch")), provenance),
            new("head_sha", JsonValue.Create(headSha), provenance),
            new("run_number", JsonValue.Create(RequireNumber(document, root, "run_number")), provenance),
            new("event", JsonValue.Create(RequireString(document, root, "event")), provenance),
            new("status", JsonValue.Create(RequireString(document, root, "status")), provenance),
            new("conclusion", JsonValue.Create(RequireString(document, root, "conclusion")), provenance),
            new("created_at", JsonValue.Create(RequireString(document, root, "created_at")), provenance),
            new("updated_at", JsonValue.Create(RequireString(document, root, "updated_at")), provenance),
        };

        var edgeProvenance = new FactProvenance(Record(document));
        return new NormalizedState()
            .AddNode(new NormalizedNode($"build-run:{runId}", NodeType("BuildRun"), properties, provenance))
            .AddEdge(new NormalizedEdge(
                "built-from",
                $"build-run:{runId}",
                NodeType("BuildRun"),
                $"commit:{headSha}",
                NodeType("Commit"),
                edgeProvenance))
            .AddEdge(new NormalizedEdge(
                "committed-to",
                $"commit:{headSha}",
                NodeType("Commit"),
                $"repository:{repositoryName}",
                NodeType("SourceRepository"),
                edgeProvenance));
    }
}
