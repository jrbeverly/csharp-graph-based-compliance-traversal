using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Adapters;

/// <summary>
/// Normalizes a mocked AWS S3 GetBucketEncryption response into the observed
/// server-side encryption configuration of one bucket. The response body
/// carries no bucket identity (the real API identifies the bucket through the
/// request path, not the body), so the adapter is bound to the bucket it was
/// queried for — exactly the knowledge a live connector has when it calls the
/// API.
/// </summary>
public sealed class AwsGetBucketEncryptionAdapter : ExternalSourceAdapter
{
    public AwsGetBucketEncryptionAdapter(string bucketId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketId);
        BucketId = bucketId;
    }

    /// <summary>The stable id of the bucket the response was queried for.</summary>
    public string BucketId { get; }

    /// <inheritdoc/>
    public override string Name => "aws.s3.get-bucket-encryption";

    /// <inheritdoc/>
    public override SourceKind SourceKind => SourceKind.AwsObserved;

    /// <inheritdoc/>
    public override bool CanNormalize(RepositoryDocument document) =>
        document.Content is JsonObject root && root["ServerSideEncryptionConfiguration"] is JsonObject;

    /// <inheritdoc/>
    public override NormalizedState Normalize(RepositoryDocument document, IReadOnlyList<RepositoryDocument> availableDocuments)
    {
        var root = RequireRoot(document);
        var configuration = RequireObject(document, root, "ServerSideEncryptionConfiguration");
        var rules = RequireArray(document, configuration, "Rules");
        if (rules.Count == 0)
        {
            throw Invalid(document, "the encryption configuration must carry at least one rule.");
        }

        var rule = RequireElementObject(document, rules[0], "ServerSideEncryptionConfiguration.Rules");
        var byDefault = RequireObject(document, rule, "ApplyServerSideEncryptionByDefault");
        var algorithm = RequireString(document, byDefault, "SSEAlgorithm");

        var encryption = new JsonObject { ["algorithm"] = algorithm };
        if (byDefault.TryGetPropertyValue("KMSMasterKeyID", out var key) && key is JsonValue keyArn)
        {
            encryption["key_arn"] = keyArn.DeepClone();
        }
        else if (algorithm == "aws:kms")
        {
            throw Invalid(document, "SSEAlgorithm 'aws:kms' must carry a KMSMasterKeyID.");
        }

        if (rule.TryGetPropertyValue("BucketKeyEnabled", out var bucketKey) && bucketKey is JsonValue bucketKeyEnabled)
        {
            encryption["bucket_key_enabled"] = bucketKeyEnabled.DeepClone();
        }

        var provenance = new FactProvenance(Record(document));
        return new NormalizedState().AddNode(new NormalizedNode(
            BucketId,
            NodeType("S3Bucket"),
            [new NormalizedProperty("encryption", encryption, provenance)],
            provenance));
    }
}
