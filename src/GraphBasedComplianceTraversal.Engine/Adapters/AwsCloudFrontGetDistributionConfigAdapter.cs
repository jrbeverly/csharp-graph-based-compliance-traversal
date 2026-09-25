using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Adapters;

/// <summary>
/// Normalizes a mocked AWS CloudFront GetDistributionConfig response into the
/// observed configuration of one distribution and the observed origin edge to
/// its S3 bucket. The response body identifies the origin bucket by its
/// domain name but never names the distribution itself (the real API takes the
/// distribution id from the request path), so the adapter is bound to the
/// distribution it was queried for.
/// </summary>
public sealed class AwsCloudFrontGetDistributionConfigAdapter : ExternalSourceAdapter
{
    public AwsCloudFrontGetDistributionConfigAdapter(string distributionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distributionId);
        DistributionId = distributionId;
    }

    /// <summary>The stable id of the distribution the response was queried for.</summary>
    public string DistributionId { get; }

    /// <inheritdoc/>
    public override string Name => "aws.cloudfront.get-distribution-config";

    /// <inheritdoc/>
    public override SourceKind SourceKind => SourceKind.AwsObserved;

    /// <inheritdoc/>
    public override bool CanNormalize(RepositoryDocument document) =>
        document.Content is JsonObject root && root["DistributionConfig"] is JsonObject;

    /// <inheritdoc/>
    public override NormalizedState Normalize(RepositoryDocument document, IReadOnlyList<RepositoryDocument> availableDocuments)
    {
        var root = RequireRoot(document);
        var config = RequireObject(document, root, "DistributionConfig");
        var defaultCacheBehavior = RequireObject(document, config, "DefaultCacheBehavior");
        var viewerCertificate = RequireObject(document, config, "ViewerCertificate");
        var origins = RequireObject(document, config, "Origins");

        var targetOriginId = RequireString(document, defaultCacheBehavior, "TargetOriginId");
        var originItems = RequireArray(document, origins, "Items");
        var targetOrigin = originItems
            .Select(item => RequireElementObject(document, item, "DistributionConfig.Origins.Items"))
            .FirstOrDefault(item =>
                item.TryGetPropertyValue("Id", out var id)
                && id is JsonValue idValue
                && string.Equals(idValue.ToString(), targetOriginId, StringComparison.Ordinal))
            ?? throw Invalid(document, $"no origin with id '{targetOriginId}' is listed in the distribution config.");

        var domainName = RequireString(document, targetOrigin, "DomainName");
        var bucketName = BucketNameFromDomainName(document, domainName);

        var normalizedOrigins = new JsonArray();
        foreach (var item in originItems)
        {
            var origin = RequireElementObject(document, item, "DistributionConfig.Origins.Items");
            var normalized = new JsonObject
            {
                ["id"] = JsonValue.Create(RequireString(document, origin, "Id")),
                ["domain_name"] = JsonValue.Create(RequireString(document, origin, "DomainName")),
            };
            if (origin.TryGetPropertyValue("OriginPath", out var originPath) && originPath is not null)
            {
                normalized["origin_path"] = originPath.DeepClone();
            }

            normalizedOrigins.Add(normalized);
        }

        var provenance = new FactProvenance(Record(document));
        var properties = new List<NormalizedProperty>
        {
            new("enabled", JsonValue.Create(RequireBoolean(document, config, "Enabled")), provenance),
            new("viewer_protocol_policy", JsonValue.Create(RequireString(document, defaultCacheBehavior, "ViewerProtocolPolicy")), provenance),
            new("minimum_protocol_version", JsonValue.Create(RequireString(document, viewerCertificate, "MinimumProtocolVersion")), provenance),
            new("http_version", JsonValue.Create(RequireString(document, config, "HttpVersion")), provenance),
            new("ipv6_enabled", JsonValue.Create(RequireBoolean(document, config, "IsIPV6Enabled")), provenance),
            new("price_class", JsonValue.Create(RequireString(document, config, "PriceClass")), provenance),
            new("origins", normalizedOrigins, provenance),
        };
        if (config.TryGetPropertyValue("Aliases", out var aliases) && aliases is JsonObject aliasesObject
            && aliasesObject.TryGetPropertyValue("Items", out var aliasItems) && aliasItems is JsonArray aliasArray)
        {
            properties.Add(new NormalizedProperty("aliases", aliasArray.DeepClone(), provenance));
        }

        return new NormalizedState()
            .AddNode(new NormalizedNode(DistributionId, NodeType("CloudFrontDistribution"), properties, provenance))
            .AddEdge(new NormalizedEdge(
                "origin",
                DistributionId,
                NodeType("CloudFrontDistribution"),
                $"aws:s3:{bucketName}",
                NodeType("S3Bucket"),
                provenance));
    }

    private static string BucketNameFromDomainName(RepositoryDocument document, string domainName)
    {
        // S3 origin domains read "<bucket>.s3[.<region>].amazonaws.com".
        const string marker = ".s3.";
        var markerIndex = domainName.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex <= 0)
        {
            throw Invalid(document, $"origin domain '{domainName}' is not an S3 bucket domain.");
        }

        return domainName[..markerIndex];
    }
}
