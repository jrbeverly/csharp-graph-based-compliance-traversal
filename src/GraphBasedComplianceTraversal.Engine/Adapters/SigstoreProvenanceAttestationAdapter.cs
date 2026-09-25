using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Adapters;

/// <summary>
/// Normalizes a mocked Sigstore/SLSA provenance attestation into the derived
/// linkage between an artifact and the build run, commit, and repository it
/// came from. The attestation binds the artifact digest to a workflow run by
/// its invocation id, so the adapter resolves the workflow-run document from
/// the available documents — the same resolution a live connector performs
/// against the CI API — and reads the run's head commit and repository from
/// it. The adapter is bound to the artifact the attestation attests: the
/// attestation identifies its subject by digest, and the mock world's
/// attestation attests <c>customer-agent</c> 2.8.4 (see the fixture's
/// <c>$description</c>).
/// </summary>
public sealed class SigstoreProvenanceAttestationAdapter : ExternalSourceAdapter
{
    private const string ExpectedPayloadType = "application/vnd.in-toto+json";
    private const string ExpectedPredicateType = "https://slsa.dev/provenance/v1";
    private const string AttestationVersion = "slsa-provenance/v1";

    public SigstoreProvenanceAttestationAdapter(string artifactId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArtifactId = artifactId;
    }

    /// <summary>The stable id of the artifact the attestation attests.</summary>
    public string ArtifactId { get; }

    /// <inheritdoc/>
    public override string Name => "sigstore.provenance-attestation";

    /// <inheritdoc/>
    public override SourceKind SourceKind => SourceKind.ProvenanceAttestation;

    /// <inheritdoc/>
    public override bool CanNormalize(RepositoryDocument document) =>
        document.Content is JsonObject root
        && root["payloadType"] is JsonValue
        && root["payload"] is JsonObject payload
        && payload["predicate"] is JsonObject;

    /// <inheritdoc/>
    public override NormalizedState Normalize(RepositoryDocument document, IReadOnlyList<RepositoryDocument> availableDocuments)
    {
        var root = RequireRoot(document);
        var payloadType = RequireString(document, root, "payloadType");
        if (payloadType != ExpectedPayloadType)
        {
            throw Invalid(document, $"payloadType must be '{ExpectedPayloadType}', got '{payloadType}'.");
        }

        var payload = RequireObject(document, root, "payload");
        var predicateType = RequireString(document, payload, "predicateType");
        if (predicateType != ExpectedPredicateType)
        {
            throw Invalid(document, $"predicateType must be '{ExpectedPredicateType}', got '{predicateType}'.");
        }

        var digest = SubjectDigest(document, payload);
        var predicate = RequireObject(document, payload, "predicate");
        var buildDefinition = RequireObject(document, predicate, "buildDefinition");
        var externalParameters = RequireObject(document, buildDefinition, "externalParameters");
        var workflow = RequireObject(document, externalParameters, "workflow");

        var workflowRepository = RepositoryFromUrl(document, RequireString(document, workflow, "repository"));
        var runDetails = RequireObject(document, predicate, "runDetails");
        var metadata = RequireObject(document, runDetails, "metadata");
        var invocationId = RequireString(document, metadata, "invocationId");
        var (invocationRepository, runId) = Invocation(document, invocationId);
        if (invocationRepository != workflowRepository)
        {
            throw Invalid(document,
                $"the attestation's workflow repository '{workflowRepository}' does not match the run's repository '{invocationRepository}'.");
        }

        // The attestation names the run by its invocation id; the run's head
        // commit and repository come from the workflow-run document. A live
        // connector fetches that document from the CI API.
        var runDocument = availableDocuments.FirstOrDefault(candidate =>
            candidate.Content is JsonObject candidateRoot
            && candidateRoot["id"] is JsonValue id
            && id.TryGetValue<long>(out var candidateRunId)
            && candidateRunId == runId)
            ?? throw new AdapterException(
                document.RelativePath,
                $"the attestation references workflow run {runId}, but no available document carries that run.");

        var runRoot = (JsonObject)runDocument.Content;
        var headSha = RequireString(runDocument, runRoot, "head_sha");
        RequireHexSha(runDocument, headSha, "head_sha");
        var runRepository = RequireObject(runDocument, runRoot, "repository");
        var runFullName = RequireString(runDocument, runRepository, "full_name");
        if (runFullName != invocationRepository)
        {
            throw Invalid(document,
                $"the workflow run {runId} belongs to repository '{runFullName}', but the attestation references '{invocationRepository}'.");
        }

        var repositoryName = RepositoryNameFromFullName(runDocument, runFullName);
        var createdAt = RequireTimestamp(runDocument, runRoot, "created_at");

        var attestationRecord = Record(document, version: AttestationVersion);
        var ciRecord = new ProvenanceRecord(SourceKind.Ci, runDocument.RelativePath, createdAt);
        var ciEdgeRecord = new ProvenanceRecord(SourceKind.Ci, runDocument.RelativePath);

        return new NormalizedState()
            .AddNode(new NormalizedNode(
                ArtifactId,
                NodeType("Artifact"),
                [new NormalizedProperty("digest", JsonValue.Create(digest), new FactProvenance(attestationRecord))],
                new FactProvenance(attestationRecord)))
            .AddEdge(new NormalizedEdge(
                "produced-by",
                ArtifactId,
                NodeType("Artifact"),
                $"build-run:{runId}",
                NodeType("BuildRun"),
                new FactProvenance(attestationRecord, ciRecord)))
            .AddEdge(new NormalizedEdge(
                "built-from",
                $"build-run:{runId}",
                NodeType("BuildRun"),
                $"commit:{headSha}",
                NodeType("Commit"),
                new FactProvenance(ciEdgeRecord)))
            .AddEdge(new NormalizedEdge(
                "committed-to",
                $"commit:{headSha}",
                NodeType("Commit"),
                $"repository:{repositoryName}",
                NodeType("SourceRepository"),
                new FactProvenance(ciEdgeRecord)));
    }

    private static string SubjectDigest(RepositoryDocument document, JsonObject payload)
    {
        var subjects = RequireArray(document, payload, "subject");
        if (subjects.Count != 1)
        {
            throw Invalid(document, $"the attestation must carry exactly one subject, got {subjects.Count}.");
        }

        var subject = RequireElementObject(document, subjects[0], "subject");
        var digest = RequireObject(document, subject, "digest");
        var sha256 = RequireString(document, digest, "sha256");
        // The mock world's digests are abbreviated, so the adapter validates
        // the digest's shape (a hexadecimal content hash), not its
        // cryptographic length — that is a verifier's concern.
        if (sha256.Length < 16 || !sha256.All(Uri.IsHexDigit))
        {
            throw Invalid(document, $"the subject digest 'sha256:{sha256}' is not a hexadecimal content hash.");
        }

        return $"sha256:{sha256}";
    }

    private static (string Repository, long RunId) Invocation(RepositoryDocument document, string invocationId)
    {
        var path = UriPath(document, invocationId);
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        // https://github.com/<owner>/<name>/actions/runs/<id>/attempts/<n>
        var runsIndex = Array.IndexOf(segments, "runs");
        if (runsIndex < 2
            || segments[runsIndex - 1] != "actions"
            || runsIndex + 2 >= segments.Length
            || segments[runsIndex + 2] != "attempts"
            || !long.TryParse(segments[runsIndex + 1], out var runId)
            || runId <= 0)
        {
            throw Invalid(document, $"invocationId '{invocationId}' does not name a workflow run.");
        }

        return ($"{segments[0]}/{segments[1]}", runId);
    }

    private static string RepositoryFromUrl(RepositoryDocument document, string url)
    {
        var path = UriPath(document, url);
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            throw Invalid(document, $"repository url '{url}' is not an owner/name url.");
        }

        return $"{segments[0]}/{segments[1]}";
    }

    private static string UriPath(RepositoryDocument document, string url)
    {
        Uri uri;
        try
        {
            uri = new Uri(url, UriKind.Absolute);
        }
        catch (UriFormatException)
        {
            throw Invalid(document, $"'{url}' is not a valid url.");
        }

        return uri.AbsolutePath;
    }
}
