using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.State;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

public sealed class FactSubjectTests
{
    private const string KmsEncryption = """
        {"algorithm":"aws:kms","key_arn":"arn:aws:kms:us-east-1:123456789012:key/abcdef12-3456-7890-abcd-ef1234567890"}
        """;

    // Pins the shared state model consumed by later milestones; grow this list
    // only by extending the enum and updating docs/state-model.md.
    private static readonly string[] ExpectedValidationStateNames =
        ["Unknown", "Pass", "Fail", "NotApplicable", "Exception", "Expired", "Stale", "Conflicting"];

    private static readonly string[] ExpectedResolvedPropertyNames = ["encryption", "region", "versioning"];
    private static readonly ValidationState[] ExpectedResolvedStates =
        [ValidationState.Unknown, ValidationState.Pass, ValidationState.Unknown];

    [Fact]
    public void ValidationStateHasExactlyTheDeclaredMembersWithUnknownAsTheDefault()
    {
        Assert.Equal(ExpectedValidationStateNames, Enum.GetNames<ValidationState>());

        // A slot that was never resolved reads UNKNOWN, never PASS, by construction.
        Assert.Equal(ValidationState.Unknown, default(ValidationState));
    }

    [Fact]
    public void DeclaredAndObservedValuesForTheSamePropertyCoexist()
    {
        var subject = new FactSubject("aws:s3:prod-release-artifacts");

        subject.Declare("encryption", Json(KmsEncryption));
        subject.Observe("encryption", Json("""{"algorithm":"AES256"}"""));

        Assert.True(JsonNode.DeepEquals(Json(KmsEncryption), subject.Declared["encryption"]));
        Assert.True(JsonNode.DeepEquals(Json("""{"algorithm":"AES256"}"""), subject.Observed["encryption"]));
    }

    [Fact]
    public void DeclaringNeverOverwritesObservedAndObservingNeverOverwritesDeclared()
    {
        var subject = new FactSubject("aws:s3:prod-release-artifacts");

        subject.Observe("encryption", Json("""{"algorithm":"AES256"}"""));
        subject.Declare("encryption", Json(KmsEncryption));
        subject.Observe("encryption", Json("""{"algorithm":"AES256"}"""));

        Assert.True(JsonNode.DeepEquals(Json(KmsEncryption), subject.Declared["encryption"]));
        Assert.True(JsonNode.DeepEquals(Json("""{"algorithm":"AES256"}"""), subject.Observed["encryption"]));
    }

    [Fact]
    public void APropertyWithNoObservationResolvesToUnknownEvenWhenDeclared()
    {
        var subject = new FactSubject("aws:s3:prod-release-artifacts");

        subject.Declare("public_access_block", JsonValue.Create(true));

        var resolved = subject.Resolve("public_access_block");

        Assert.Equal(ValidationState.Unknown, resolved.State);
        Assert.True(resolved.HasDeclaration);
        Assert.False(resolved.HasObservation);
        // The declaration itself stays visible; it just cannot pass on its own.
        Assert.True(JsonNode.DeepEquals(JsonValue.Create(true), resolved.DeclaredValue));
    }

    [Fact]
    public void APropertyWithNothingAtAllResolvesToUnknown()
    {
        var subject = new FactSubject("aws:s3:prod-release-artifacts");

        var resolved = subject.Resolve("encryption");

        Assert.Equal(ValidationState.Unknown, resolved.State);
        Assert.False(resolved.HasDeclaration);
        Assert.False(resolved.HasObservation);
    }

    [Fact]
    public void AnObservedPropertyWithNoDeclarationResolvesToUnknown()
    {
        var subject = new FactSubject("aws:s3:prod-release-artifacts");

        subject.Observe("aws_account", JsonValue.Create("123456789012"));

        var resolved = subject.Resolve("aws_account");

        // There is no declared expectation to validate the observation against,
        // so the observation alone cannot pass.
        Assert.Equal(ValidationState.Unknown, resolved.State);
        Assert.False(resolved.HasDeclaration);
        Assert.True(resolved.HasObservation);
    }

    [Fact]
    public void AJsonNullObservationCountsAsAbsent()
    {
        var subject = new FactSubject("aws:s3:prod-release-artifacts");

        subject.Declare("encryption", Json(KmsEncryption));
        subject.Observe("encryption", null);

        var resolved = subject.Resolve("encryption");

        Assert.Equal(ValidationState.Unknown, resolved.State);
    }

    [Fact]
    public void EqualDeclaredAndObservedValuesResolveToPass()
    {
        var subject = new FactSubject("aws:s3:prod-release-artifacts");

        subject.Declare("encryption", Json(KmsEncryption));
        subject.Observe("encryption", Json(KmsEncryption));

        var resolved = subject.Resolve("encryption");

        Assert.Equal(ValidationState.Pass, resolved.State);
        Assert.True(resolved.HasDeclaration);
        Assert.True(resolved.HasObservation);
    }

    [Fact]
    public void DifferingDeclaredAndObservedValuesResolveToConflictingPreservingBoth()
    {
        var subject = new FactSubject("aws:s3:prod-release-artifacts");

        subject.Declare("encryption", Json(KmsEncryption));
        subject.Observe("encryption", Json("""{"algorithm":"AES256"}"""));

        var resolved = subject.Resolve("encryption");

        Assert.Equal(ValidationState.Conflicting, resolved.State);
        Assert.Equal("encryption", resolved.PropertyName);
        // Both values remain visible; the conflict hides neither of them.
        Assert.True(JsonNode.DeepEquals(Json(KmsEncryption), resolved.DeclaredValue));
        Assert.True(JsonNode.DeepEquals(Json("""{"algorithm":"AES256"}"""), resolved.ObservedValue));
    }

    [Fact]
    public void StructuredValuesCompareDeeplyRatherThanByReference()
    {
        var subject = new FactSubject("aws:s3:prod-release-artifacts");

        // Same structure, different node instances: still equal.
        subject.Declare("encryption", Json(KmsEncryption));
        subject.Observe("encryption", Json(KmsEncryption));

        Assert.Equal(ValidationState.Pass, subject.Resolve("encryption").State);

        // Same property order, one differing field: still conflicting.
        subject.Observe(
            "encryption",
            Json("""{"algorithm":"aws:kms","key_arn":"arn:aws:kms:us-east-1:123456789012:key/00000000-0000-0000-0000-000000000000"}"""));

        Assert.Equal(ValidationState.Conflicting, subject.Resolve("encryption").State);
    }

    [Fact]
    public void ResolveAllCoversTheUnionOfBothFactSetsInOrder()
    {
        var subject = new FactSubject("aws:s3:prod-release-artifacts");

        subject.Declare("region", JsonValue.Create("us-east-1"));
        subject.Declare("versioning", JsonValue.Create("enabled"));
        subject.Observe("region", JsonValue.Create("us-east-1"));
        subject.Observe("encryption", Json("""{"algorithm":"AES256"}"""));

        var resolutions = subject.ResolveAll();

        Assert.Equal(
            ExpectedResolvedPropertyNames,
            resolutions.Select(resolved => resolved.PropertyName).ToArray());
        Assert.Equal(
            ExpectedResolvedStates,
            resolutions.Select(resolved => resolved.State).ToArray());
    }

    [Fact]
    public void ASubjectConstructedWithSeedFactsKeepsBothSets()
    {
        var subject = new FactSubject(
            "aws:s3:prod-release-artifacts",
            declared: new Dictionary<string, JsonNode?> { ["encryption"] = Json(KmsEncryption) },
            observed: new Dictionary<string, JsonNode?> { ["encryption"] = Json("""{"algorithm":"AES256"}""") });

        var resolved = subject.Resolve("encryption");

        Assert.Equal(ValidationState.Conflicting, resolved.State);
        Assert.True(JsonNode.DeepEquals(Json(KmsEncryption), subject.Declared["encryption"]));
        Assert.True(JsonNode.DeepEquals(Json("""{"algorithm":"AES256"}"""), subject.Observed["encryption"]));
    }

    [Fact]
    public void RejectsAnEmptySubjectIdOrPropertyName()
    {
        Assert.Throws<ArgumentException>(() => new FactSubject(" "));
        Assert.Throws<ArgumentException>(() => new FactSubject("ok").Declare(" ", JsonValue.Create(true)));
        Assert.Throws<ArgumentException>(() => new FactSubject("ok").Observe(" ", JsonValue.Create(true)));
        Assert.Throws<ArgumentException>(() => new FactSubject("ok").Resolve(string.Empty));
    }

    private static JsonNode Json(string json) =>
        JsonNode.Parse(json) ?? throw new InvalidOperationException("Test value must parse to a non-null JSON node.");
}
