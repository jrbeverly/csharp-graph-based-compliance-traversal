using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.IO;

namespace GraphBasedComplianceTraversal.Engine.Adapters;

/// <summary>
/// The mock fixture reader: the ordered set of concrete adapters that
/// normalize the repository's <c>fixtures/**</c> documents, each bound to the
/// subject the repository's world describes. This is the piece a future live
/// connector replaces — it enumerates the mocked responses and dispatches each
/// to its adapter, where a connector would call the external API and feed the
/// parsed response through the same adapters. Downstream code consumes
/// normalized states either way.
/// </summary>
public sealed class FixtureAdapterRegistry
{
    private readonly IReadOnlyList<IExternalSourceAdapter> _adapters;

    public FixtureAdapterRegistry(IReadOnlyList<IExternalSourceAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        if (adapters.Count == 0)
        {
            throw new ArgumentException("At least one adapter must be registered.", nameof(adapters));
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var adapter in adapters)
        {
            ArgumentNullException.ThrowIfNull(adapter);
            if (!names.Add(adapter.Name))
            {
                throw new ArgumentException($"Adapter name '{adapter.Name}' is registered more than once.", nameof(adapters));
            }
        }

        _adapters = adapters.ToArray();
    }

    /// <summary>
    /// The adapters wired to the repository's mocked external world. The
    /// constructor bindings carry the subject identities the mock responses do
    /// not state themselves — the bucket the encryption response was queried
    /// for, the distribution the config response was queried for, and the
    /// artifact the attestation attests — exactly the knowledge a live
    /// connector has from the request it made.
    /// </summary>
    public static FixtureAdapterRegistry MockWorld { get; } = new(
    [
        new AwsCloudFrontGetDistributionConfigAdapter("aws:cloudfront:customer-downloads"),
        new AwsGetBucketEncryptionAdapter("aws:s3:prod-release-artifacts"),
        new AwsGetBucketPolicyAdapter(),
        new CiWorkflowRunAdapter(),
        new GithubCommitAdapter(),
        new GithubRepositoryAdapter(),
        new SigstoreProvenanceAttestationAdapter("artifact:customer-agent-2.8.4"),
        new TerraformStateFragmentAdapter(),
    ]);

    /// <summary>The registered adapters, in registration order.</summary>
    public IReadOnlyList<IExternalSourceAdapter> Adapters => _adapters;

    /// <summary>
    /// The adapter whose response shape matches the document, or <c>null</c>
    /// when none does. The registered shape checks are mutually exclusive, so
    /// at most one adapter matches.
    /// </summary>
    public IExternalSourceAdapter? FindAdapter(RepositoryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _adapters.FirstOrDefault(adapter => adapter.CanNormalize(document));
    }

    /// <summary>
    /// Normalizes every fixture document into its adapter's state, in document
    /// order. A document no adapter matches is an error, never a silent skip.
    /// </summary>
    public IReadOnlyList<NormalizedState> NormalizeAll(IReadOnlyList<RepositoryDocument> fixtures)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        var states = new List<NormalizedState>();
        foreach (var fixture in fixtures)
        {
            var adapter = FindAdapter(fixture)
                ?? throw new AdapterException(fixture.RelativePath, "no adapter normalizes this response shape.");
            states.Add(adapter.Normalize(fixture, fixtures));
        }

        return states;
    }

    /// <summary>
    /// Normalizes every fixture document and applies the resulting facts into
    /// the graph, in document order.
    /// </summary>
    public void ApplyAll(TypedPropertyGraph graph, IReadOnlyList<RepositoryDocument> fixtures)
    {
        ArgumentNullException.ThrowIfNull(graph);
        foreach (var state in NormalizeAll(fixtures))
        {
            state.ApplyTo(graph);
        }
    }
}
