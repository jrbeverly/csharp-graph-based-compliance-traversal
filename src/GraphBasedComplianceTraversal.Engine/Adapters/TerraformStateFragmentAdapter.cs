using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Adapters;

/// <summary>
/// Normalizes a mocked Terraform state fragment into the applied
/// infrastructure facts of one bucket: the base <c>aws_s3_bucket</c> resource
/// carries the bucket's identity, and the companion versioning, encryption,
/// and public-access-block resources become its configuration properties.
/// These are declared-infrastructure facts — what infrastructure-as-code has
/// applied — so they are tagged <see cref="SourceKind.Terraform"/> rather
/// than observed.
/// </summary>
public sealed class TerraformStateFragmentAdapter : ExternalSourceAdapter
{
    /// <inheritdoc/>
    public override string Name => "terraform.state-fragment";

    /// <inheritdoc/>
    public override SourceKind SourceKind => SourceKind.Terraform;

    /// <inheritdoc/>
    public override bool CanNormalize(RepositoryDocument document) =>
        document.Content is JsonObject root
        && root["module"] is JsonValue
        && root["resources"] is JsonArray;

    /// <inheritdoc/>
    public override NormalizedState Normalize(RepositoryDocument document, IReadOnlyList<RepositoryDocument> availableDocuments)
    {
        var root = RequireRoot(document);
        var resources = RequireArray(document, root, "resources")
            .Select(item => RequireElementObject(document, item, "resources"))
            .ToArray();

        var bucketResource = SingleResource(document, resources, "aws_s3_bucket");
        var bucketAttributes = ResourceAttributes(document, bucketResource);
        var bucketName = RequireString(document, bucketAttributes, "bucket");
        var bucketId = $"aws:s3:{bucketName}";

        var provenance = new FactProvenance(Record(document));
        var properties = new List<NormalizedProperty>
        {
            new("arn", JsonValue.Create(RequireString(document, bucketAttributes, "arn")), provenance),
            new("force_destroy", JsonValue.Create(RequireBoolean(document, bucketAttributes, "force_destroy")), provenance),
        };
        if (bucketAttributes.TryGetPropertyValue("tags", out var tags) && tags is not null)
        {
            properties.Add(new NormalizedProperty("tags", tags.DeepClone(), provenance));
        }

        var versioning = FindResource(resources, "aws_s3_bucket_versioning");
        if (versioning is not null)
        {
            var attributes = ResourceAttributes(document, versioning);
            var configuration = RequireArray(document, attributes, "versioning_configuration");
            if (configuration.Count > 0)
            {
                var status = RequireString(document, RequireElementObject(document, configuration[0], "versioning_configuration"), "status");
                properties.Add(new NormalizedProperty("versioning", JsonValue.Create(status.ToLowerInvariant()), provenance));
            }
        }

        var encryption = FindResource(resources, "aws_s3_bucket_server_side_encryption_configuration");
        if (encryption is not null)
        {
            properties.Add(new NormalizedProperty("encryption", NormalizeEncryption(document, encryption), provenance));
        }

        var publicAccessBlock = FindResource(resources, "aws_s3_bucket_public_access_block");
        if (publicAccessBlock is not null)
        {
            properties.Add(new NormalizedProperty("public_access_block", NormalizePublicAccessBlock(document, publicAccessBlock), provenance));
        }

        return new NormalizedState().AddNode(new NormalizedNode(bucketId, NodeType("S3Bucket"), properties, provenance));
    }

    private static JsonObject? FindResource(IEnumerable<JsonObject> resources, string type) =>
        resources.FirstOrDefault(resource =>
            resource.TryGetPropertyValue("type", out var typeNode)
            && typeNode is JsonValue typeValue
            && string.Equals(typeValue.ToString(), type, StringComparison.Ordinal));

    private static JsonObject SingleResource(RepositoryDocument document, IReadOnlyList<JsonObject> resources, string type)
    {
        var matches = resources.Where(resource =>
            resource.TryGetPropertyValue("type", out var typeNode)
            && typeNode is JsonValue typeValue
            && string.Equals(typeValue.ToString(), type, StringComparison.Ordinal)).ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            > 1 => throw Invalid(document, $"the state fragment declares {matches.Length} '{type}' resources; exactly one is expected."),
            _ => throw Invalid(document, $"the state fragment declares no '{type}' resource."),
        };
    }

    private static JsonObject ResourceAttributes(RepositoryDocument document, JsonObject resource)
    {
        var instances = RequireArray(document, resource, "instances");
        if (instances.Count == 0)
        {
            throw Invalid(document, $"resource '{resource["type"]}' carries no instances.");
        }

        var instance = RequireElementObject(document, instances[0], "instances");
        return RequireObject(document, instance, "attributes");
    }

    private static JsonObject NormalizeEncryption(RepositoryDocument document, JsonObject resource)
    {
        var attributes = ResourceAttributes(document, resource);
        var rules = RequireArray(document, attributes, "rule");
        if (rules.Count == 0)
        {
            throw Invalid(document, $"resource '{resource["type"]}' carries no encryption rule.");
        }

        var rule = RequireElementObject(document, rules[0], "rule");
        var defaults = RequireArray(document, rule, "apply_server_side_encryption_by_default");
        if (defaults.Count == 0)
        {
            throw Invalid(document, "the encryption rule carries no default encryption configuration.");
        }

        var byDefault = RequireElementObject(document, defaults[0], "apply_server_side_encryption_by_default");
        var normalized = new JsonObject
        {
            ["algorithm"] = JsonValue.Create(RequireString(document, byDefault, "sse_algorithm")),
        };
        if (byDefault.TryGetPropertyValue("kms_master_key_id", out var keyId) && keyId is not null)
        {
            normalized["key_arn"] = keyId.DeepClone();
        }

        if (rule.TryGetPropertyValue("bucket_key_enabled", out var bucketKeyEnabled) && bucketKeyEnabled is not null)
        {
            normalized["bucket_key_enabled"] = bucketKeyEnabled.DeepClone();
        }

        return normalized;
    }

    private static JsonValue NormalizePublicAccessBlock(RepositoryDocument document, JsonObject resource)
    {
        var attributes = ResourceAttributes(document, resource);
        var blocks = new[]
        {
            RequireBoolean(document, attributes, "block_public_acls"),
            RequireBoolean(document, attributes, "block_public_policy"),
            RequireBoolean(document, attributes, "ignore_public_acls"),
            RequireBoolean(document, attributes, "restrict_public_buckets"),
        };

        // The block is effective only when all four settings are enabled.
        return JsonValue.Create(blocks.All(block => block));
    }
}
