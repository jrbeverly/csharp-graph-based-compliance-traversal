using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Adapters;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.Ingestion;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;
using GraphBasedComplianceTraversal.Engine.Reconciliation;
using GraphBasedComplianceTraversal.Engine.State;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Pins the declared-versus-observed reconciliation: the adapters' observed
/// facts are attached to the declared subjects they describe, agreeing facts
/// are recorded as corroboration carrying both provenances, and disagreeing
/// facts are recorded <see cref="ValidationState.Conflicting"/> carrying both
/// values, both provenances, and the source-authority precedence — never
/// silently reconciled to one value. The scenarios run the full pipeline over
/// the repository's own world and over the curated declared/observed mismatch
/// world (negative path).
/// </summary>
public sealed class ReconciliationTests
{
    private const string BucketId = "aws:s3:prod-release-artifacts";
    private const string BucketDeclarationLocator = "data/cloud/aws/resources/s3-prod-release-artifacts.yaml";
    private const string TerraformLocator = "fixtures/terraform/release-storage-resource.json";
    private const string AwsEncryptionLocator = "fixtures/aws/s3/get-bucket-encryption-response.json";
    private const string AwsPolicyLocator = "fixtures/aws/s3/get-bucket-policy-response.json";
    private const string GithubRepositoryLocator = "fixtures/github/repository-response.json";
    private const string AttestationLocator = "fixtures/provenance/sigstore-attestation.json";

    [Fact]
    public void TheS3BucketNodeCarriesItsDeclaredAndObservedConfigurationsEachWithDistinctProvenance()
    {
        var graph = ReconcileRealWorld();
        var bucket = graph.GetNode(BucketId);

        // The declared configuration, traceable to the organization's record.
        var declaredEncryption = bucket.Properties["encryption"]!;
        Assert.Equal(
            """{"algorithm":"aws:kms","key_arn":"arn:aws:kms:us-east-1:123456789012:key/abcdef12-3456-7890-abcd-ef1234567890","bucket_key_enabled":true}""",
            declaredEncryption.ToJsonString());
        var encryptionProvenance = bucket.GetPropertyProvenance("encryption").Sources;
        Assert.Contains(encryptionProvenance, record =>
            record.Kind == SourceKind.Declaration && record.Locator == BucketDeclarationLocator);

        // The AWS-observed configuration, with its own distinct provenance:
        // the observed encryption statement and the observed policy, both
        // traceable to the mocked API responses.
        Assert.Contains(encryptionProvenance, record =>
            record.Kind == SourceKind.AwsObserved && record.Locator == AwsEncryptionLocator);
        Assert.Contains(encryptionProvenance, record =>
            record.Kind == SourceKind.Terraform && record.Locator == TerraformLocator);
        var policyProvenance = bucket.GetPropertyProvenance("policy").Sources;
        Assert.Equal(SourceKind.AwsObserved, Assert.Single(policyProvenance).Kind);
        Assert.Equal(AwsPolicyLocator, policyProvenance[0].Locator);

        // The two sides are separately attributable on the reconciliation
        // record: the declared side names the record, the observed side names
        // the two external statements that corroborate it.
        var encryption = Assert.Single(graph.GetReconciliations(BucketId), fact => fact.PropertyName == "encryption");
        Assert.True(encryption.IsCorroborated);
        Assert.Equal(SourceKind.Declaration, Assert.Single(encryption.DeclaredProvenance.Sources).Kind);
        Assert.Equal(BucketDeclarationLocator, encryption.DeclaredProvenance.Sources[0].Locator);
        Assert.Equal(
            [SourceKind.AwsObserved, SourceKind.Terraform],
            encryption.ObservedProvenance.Sources.Select(record => record.Kind).ToArray());
        Assert.True(JsonNode.DeepEquals(encryption.DeclaredValue, encryption.ObservedValue));
    }

    [Fact]
    public void AnInjectedDeclaredObservedMismatchProducesAConflictingFactExposingBothSidesAndTheirSources()
    {
        var graph = ReconcileWorld(TestWorld.DerivedWorld("declared-observed-mismatch"));

        // The mismatch world declares KMS server-side encryption while its
        // mocked AWS response observes AES256: exactly one reconciliation
        // verdict, and it is a conflict — never a silently reconciled value.
        var conflict = Assert.Single(graph.Reconciliations);
        Assert.Equal(BucketId, conflict.SubjectId);
        Assert.Equal("encryption", conflict.PropertyName);
        Assert.True(conflict.IsConflicting);
        Assert.Equal(ValidationState.Conflicting, conflict.State);

        // Both sides, both values, both sources.
        Assert.Equal(
            """{"algorithm":"aws:kms","key_arn":"arn:aws:kms:us-east-1:123456789012:key/abcdef12-3456-7890-abcd-ef1234567890","bucket_key_enabled":true}""",
            conflict.DeclaredValue.ToJsonString());
        Assert.Equal(SourceKind.Declaration, Assert.Single(conflict.DeclaredProvenance.Sources).Kind);
        Assert.Equal(BucketDeclarationLocator, conflict.DeclaredProvenance.Sources[0].Locator);

        Assert.Equal("""{"algorithm":"AES256","bucket_key_enabled":false}""", conflict.ObservedValue.ToJsonString());
        Assert.Equal(SourceKind.AwsObserved, Assert.Single(conflict.ObservedProvenance.Sources).Kind);
        Assert.Equal(AwsEncryptionLocator, conflict.ObservedProvenance.Sources[0].Locator);

        // The applicable source-authority precedence is recorded, not
        // applied: for a configuration claim the live AWS observation is the
        // source to consult, and the model says so without overwriting.
        Assert.Equal(ClaimType.Configuration, conflict.ClaimType);
        Assert.Equal(SourcePrecedence.RightWins, conflict.Precedence);
        Assert.Equal(SourceKind.AwsObserved, conflict.MostAuthoritative!.Kind);
        Assert.Equal(AwsEncryptionLocator, conflict.MostAuthoritative.Locator);

        // Never overwrite: the bucket still carries the declared value, and
        // its provenance now names both statements, so the disagreement is
        // attributable from the node itself too.
        var bucket = graph.GetNode(BucketId);
        Assert.True(JsonNode.DeepEquals(conflict.DeclaredValue, bucket.Properties["encryption"]));
        Assert.Equal(
            [SourceKind.Declaration, SourceKind.AwsObserved],
            bucket.GetPropertyProvenance("encryption").Sources.Select(record => record.Kind).ToArray());
    }

    [Fact]
    public void CorroboratedFactsAreQueryableAsAgreeingAcrossIndependentSources()
    {
        var graph = ReconcileRealWorld();

        // The bucket's encryption agrees across three independent sources: the
        // organization's record, the Terraform state fragment, and the AWS
        // observation. All three are named on the reconciliation record.
        var encryption = Assert.Single(graph.GetReconciliations(BucketId), fact => fact.PropertyName == "encryption");
        Assert.Equal(ValidationState.Pass, encryption.State);
        Assert.Equal(SourceKind.Declaration, Assert.Single(encryption.DeclaredProvenance.Sources).Kind);
        Assert.Equal(
            [SourceKind.AwsObserved, SourceKind.Terraform],
            encryption.ObservedProvenance.Sources.Select(record => record.Kind).ToArray());

        // The remaining bucket configuration facts corroborate across the
        // declared record and the Terraform state.
        var versioning = Assert.Single(graph.GetReconciliations(BucketId), fact => fact.PropertyName == "versioning");
        Assert.True(versioning.IsCorroborated);
        Assert.Equal("enabled", versioning.ObservedValue.GetValue<string>());
        Assert.Equal(SourceKind.Terraform, Assert.Single(versioning.ObservedProvenance.Sources).Kind);

        var publicAccessBlock = Assert.Single(graph.GetReconciliations(BucketId), fact => fact.PropertyName == "public_access_block");
        Assert.True(publicAccessBlock.IsCorroborated);
        Assert.True(publicAccessBlock.ObservedValue.GetValue<bool>());

        var arn = Assert.Single(graph.GetReconciliations(BucketId), fact => fact.PropertyName == "arn");
        Assert.True(arn.IsCorroborated);
        Assert.Equal("arn:aws:s3:::prod-release-artifacts", arn.ObservedValue.GetValue<string>());

        // The artifact's digest agrees across the declared record and the
        // provenance attestation — a derivation claim, where the attestation
        // is the declared authority.
        var digest = Assert.Single(graph.GetReconciliations("artifact:customer-agent-2.8.4"));
        Assert.True(digest.IsCorroborated);
        Assert.Equal(ClaimType.Derivation, digest.ClaimType);
        Assert.Equal(SourceKind.ProvenanceAttestation, Assert.Single(digest.ObservedProvenance.Sources).Kind);
        Assert.Equal(AttestationLocator, digest.ObservedProvenance.Sources[0].Locator);
        Assert.Equal(SourcePrecedence.RightWins, digest.Precedence);

        // The repository's name, url, and language agree across the declared
        // record and the code-host metadata.
        foreach (var property in new[] { "name", "url", "language" })
        {
            var corroborated = Assert.Single(graph.GetReconciliations("repository:customer-agent"), fact => fact.PropertyName == property);
            Assert.True(corroborated.IsCorroborated, $"repository.{property} should corroborate.");
            Assert.Equal(SourceKind.Github, Assert.Single(corroborated.ObservedProvenance.Sources).Kind);
        }

        // Ten verdicts in total: four bucket corroborations, five repository
        // verdicts (three corroborations, two conflicts), and one artifact
        // corroboration — every property both worlds state is accounted for.
        Assert.Equal(10, graph.Reconciliations.Count);
    }

    [Fact]
    public void RealWorldDisagreementsSurfaceAsConflictingFactsCarryingBothSidesAndTheirPrecedence()
    {
        var graph = ReconcileRealWorld();
        var reconciliations = graph.GetReconciliations("repository:customer-agent");

        // The organization's record and the code host genuinely disagree about
        // the repository's description. Both texts are retained, each with its
        // source, and the organizational claim type declares the organization's
        // own record the source to consult.
        var description = Assert.Single(reconciliations, fact => fact.PropertyName == "description");
        Assert.True(description.IsConflicting);
        Assert.Contains("Primary source repository", description.DeclaredValue.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("Customer Agent — enterprise endpoint security agent", description.ObservedValue.GetValue<string>());
        Assert.Equal(SourceKind.Declaration, Assert.Single(description.DeclaredProvenance.Sources).Kind);
        Assert.Equal(SourceKind.Github, Assert.Single(description.ObservedProvenance.Sources).Kind);
        Assert.Equal(GithubRepositoryLocator, description.ObservedProvenance.Sources[0].Locator);
        Assert.Equal(ClaimType.Organization, description.ClaimType);
        Assert.Equal(SourcePrecedence.LeftWins, description.Precedence);
        Assert.Equal(SourceKind.Declaration, description.MostAuthoritative!.Kind);

        // The organization records the owning team; the code host records the
        // owning account. The disagreement is visible and attributable, not
        // resolved.
        var owner = Assert.Single(reconciliations, fact => fact.PropertyName == "owner");
        Assert.True(owner.IsConflicting);
        Assert.Equal("team:platform-engineering", owner.DeclaredValue.GetValue<string>());
        Assert.Equal("nexusdefend", owner.ObservedValue.GetValue<string>());
        Assert.Equal(SourcePrecedence.LeftWins, owner.Precedence);

        // Never overwrite: the repository node still carries the declared
        // values, with both sources named on the properties.
        var repository = graph.GetNode("repository:customer-agent");
        Assert.Equal("team:platform-engineering", repository.Properties["owner"]!.GetValue<string>());
        Assert.Contains(
            repository.GetPropertyProvenance("owner").Sources,
            record => record.Kind == SourceKind.Github && record.Locator == GithubRepositoryLocator);
    }

    [Fact]
    public void ObservedFactsAreAttachedWithoutAVerdictWhenNoDeclaredSideExists()
    {
        var graph = ReconcileRealWorld();

        // Subjects with no declared record — the build run and the commit —
        // are attached as observed facts with no reconciliation verdict.
        Assert.NotNull(graph.GetNode("build-run:84125"));
        Assert.NotNull(graph.GetNode("commit:7a94f3e8b2c1d5f6a7b8c9d0e1f2a3b4c5d6e7f8"));
        Assert.Empty(graph.GetReconciliations("build-run:84125"));
        Assert.Empty(graph.GetReconciliations("commit:7a94f3e8b2c1d5f6a7b8c9d0e1f2a3b4c5d6e7f8"));

        // Observed properties the declared record does not state — the bucket
        // policy, the repository's default branch — are attached with their
        // observed provenance and no verdict.
        Assert.Equal(SourceKind.AwsObserved, Assert.Single(
            graph.GetNode(BucketId).GetPropertyProvenance("policy").Sources).Kind);
        Assert.DoesNotContain(graph.GetReconciliations(BucketId), fact => fact.PropertyName == "policy");
        Assert.Equal(SourceKind.Github, Assert.Single(
            graph.GetNode("repository:customer-agent").GetPropertyProvenance("default_branch").Sources).Kind);

        // Reference fields the ontology maps to typed edges are relationship
        // facts, reconciled through their edges — the declared record names
        // the distribution's origin by node id while the observed config
        // states it in the API's vocabulary, so a value comparison is not a
        // verdict and no record is produced for it.
        Assert.DoesNotContain(graph.Reconciliations, fact => fact.PropertyName == "origins");
    }

    [Fact]
    public void ReconcilingTwiceDoesNotDuplicateRecordsOrOverwriteValues()
    {
        var graph = ReconcileRealWorld();
        var reconciliationsBefore = graph.Reconciliations.ToArray();
        var bucketEncryptionBefore = graph.GetNode(BucketId).Properties["encryption"]!.ToJsonString();

        // Re-running reconciliation over the already reconciled graph: the
        // observed states are applied again (idempotently) and every verdict
        // is identical, so no record is duplicated and no value moves.
        var world = RepositoryDataLoader.Load(TestWorld.RepositoryRoot);
        DeclaredObservedReconciler.ReconcileAll(graph, FixtureAdapterRegistry.MockWorld.NormalizeAll(world.Fixtures));

        Assert.Equal(reconciliationsBefore.Length, graph.Reconciliations.Count);
        Assert.Equal(bucketEncryptionBefore, graph.GetNode(BucketId).Properties["encryption"]!.ToJsonString());
    }

    [Fact]
    public void RecordingAReconciliationRequiresAStoredSubjectAndAValidState()
    {
        var graph = EmptyGraph();
        graph.AddNode(BucketId, Node("S3Bucket"));

        var valid = NewRecord(ValidationState.Pass);
        Assert.Same(valid, graph.RecordReconciliation(valid));
        Assert.Same(valid, Assert.Single(graph.GetReconciliations(BucketId)));
        Assert.Same(valid, Assert.Single(graph.Reconciliations));

        // A subject that is not a stored node cannot be reconciled.
        Assert.Throws<ArgumentException>(
            () => graph.RecordReconciliation(valid with { SubjectId = "aws:s3:no-such-bucket" }));

        // Only a property both sides state produces a verdict, and both sides
        // must carry a value.
        Assert.Throws<ArgumentOutOfRangeException>(() => NewRecord(ValidationState.Unknown));
        Assert.Throws<ArgumentNullException>(() => new FactReconciliation(
            BucketId,
            "versioning",
            ValidationState.Pass,
            null!,
            new FactProvenance(new ProvenanceRecord(SourceKind.Declaration, BucketDeclarationLocator)),
            JsonValue.Create("enabled"),
            new FactProvenance(new ProvenanceRecord(SourceKind.Terraform, TerraformLocator)),
            ClaimType.Configuration,
            SourcePrecedence.RightWins,
            new ProvenanceRecord(SourceKind.Terraform, TerraformLocator)));

        Assert.Throws<ArgumentException>(() => graph.GetReconciliations(" "));
    }

    private static TypedPropertyGraph ReconcileRealWorld() => ReconcileWorld(TestWorld.RepositoryRoot);

    private static TypedPropertyGraph ReconcileWorld(string root)
    {
        var world = RepositoryDataLoader.Load(root);
        var graph = new TypedPropertyGraph(EdgeTypeRegistry.Slice);
        DeclaredRecordsLoader.ApplyAll(graph, world.Records);
        DeclaredObservedReconciler.ReconcileAll(graph, FixtureAdapterRegistry.MockWorld.NormalizeAll(world.Fixtures));
        return graph;
    }

    private static TypedPropertyGraph EmptyGraph() => new(EdgeTypeRegistry.Slice);

    private static NodeType Node(string name) =>
        EdgeTypeRegistry.Slice.NodeTypes.Single(type => type.Name == name);

    private static FactReconciliation NewRecord(ValidationState state, JsonNode? declaredValue = null) => new(
        BucketId,
        "versioning",
        state,
        declaredValue ?? JsonValue.Create("enabled"),
        new FactProvenance(new ProvenanceRecord(SourceKind.Declaration, BucketDeclarationLocator)),
        JsonValue.Create("enabled"),
        new FactProvenance(new ProvenanceRecord(SourceKind.Terraform, TerraformLocator)),
        ClaimType.Configuration,
        SourcePrecedence.RightWins,
        new ProvenanceRecord(SourceKind.Terraform, TerraformLocator));
}
