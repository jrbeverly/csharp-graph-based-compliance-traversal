using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Adapters;

/// <summary>
/// Normalizes a mocked AWS S3 GetBucketPolicy response into the observed
/// access configuration of one bucket. Unlike the encryption response, the
/// policy body names its bucket — every statement's <c>Resource</c> is the
/// bucket's ARN — so the adapter derives the subject's stable id from the
/// response itself and rejects a policy that mixes buckets.
/// </summary>
public sealed class AwsGetBucketPolicyAdapter : ExternalSourceAdapter
{
    private const string BucketArnPrefix = "arn:aws:s3:::";

    /// <inheritdoc/>
    public override string Name => "aws.s3.get-bucket-policy";

    /// <inheritdoc/>
    public override SourceKind SourceKind => SourceKind.AwsObserved;

    /// <inheritdoc/>
    public override bool CanNormalize(RepositoryDocument document) =>
        document.Content is JsonObject root
        && root["Policy"] is JsonObject policy
        && policy["Statement"] is JsonArray;

    /// <inheritdoc/>
    public override NormalizedState Normalize(RepositoryDocument document, IReadOnlyList<RepositoryDocument> availableDocuments)
    {
        var root = RequireRoot(document);
        var policy = RequireObject(document, root, "Policy");
        var statements = RequireArray(document, policy, "Statement");
        if (statements.Count == 0)
        {
            throw Invalid(document, "the policy must carry at least one statement.");
        }

        string? bucketName = null;
        var normalizedStatements = new JsonArray();
        foreach (var item in statements)
        {
            var statement = RequireElementObject(document, item, "Policy.Statement");
            var effect = RequireString(document, statement, "Effect");
            if (effect is not "Allow" and not "Deny")
            {
                throw Invalid(document, $"statement effect must be 'Allow' or 'Deny', got '{effect}'.");
            }

            var resource = RequireString(document, statement, "Resource");
            var name = BucketNameFromResourceArn(document, resource);
            bucketName = bucketName is null
                ? name
                : string.Equals(bucketName, name, StringComparison.Ordinal)
                    ? bucketName
                    : throw Invalid(document,
                        $"statement resource '{resource}' names bucket '{name}', but earlier statements named '{bucketName}'.");

            var normalized = new JsonObject
            {
                ["effect"] = effect,
                ["actions"] = RequireArray(document, statement, "Action").DeepClone(),
                ["resource"] = resource,
            };
            if (statement.TryGetPropertyValue("Sid", out var sid) && sid is JsonValue sidValue)
            {
                normalized["sid"] = sidValue.DeepClone();
            }

            if (statement.TryGetPropertyValue("Principal", out var principal) && principal is not null)
            {
                normalized["principal"] = principal.DeepClone();
            }

            if (statement.TryGetPropertyValue("Condition", out var condition) && condition is not null)
            {
                normalized["condition"] = condition.DeepClone();
            }

            normalizedStatements.Add(normalized);
        }

        var normalizedPolicy = new JsonObject
        {
            ["version"] = policy.TryGetPropertyValue("Version", out var version) && version is not null
                ? version.DeepClone()
                : "2012-10-17",
            ["statements"] = normalizedStatements,
        };

        var provenance = new FactProvenance(Record(document));
        return new NormalizedState().AddNode(new NormalizedNode(
            $"aws:s3:{bucketName}",
            NodeType("S3Bucket"),
            [new NormalizedProperty("policy", normalizedPolicy, provenance)],
            provenance));
    }

    private static string BucketNameFromResourceArn(RepositoryDocument document, string resource)
    {
        if (!resource.StartsWith(BucketArnPrefix, StringComparison.Ordinal))
        {
            throw Invalid(document, $"resource '{resource}' is not an S3 bucket ARN ('{BucketArnPrefix}...').");
        }

        // Everything up to the first '/' is the bucket name; the rest is the
        // object key pattern the statement covers.
        var remainder = resource[BucketArnPrefix.Length..];
        var name = remainder.Split('/')[0];
        return name.Length > 0
            ? name
            : throw Invalid(document, $"resource '{resource}' does not name a bucket.");
    }
}
