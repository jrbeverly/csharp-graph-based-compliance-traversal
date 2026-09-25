using System.Text;
using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.IO;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

public sealed class RepositoryDataLoaderTests
{
    [Fact]
    public void LoadsEveryRepositoryRecordAndFixture()
    {
        var repository = RepositoryDataLoader.Load(TestWorld.RepositoryRoot);

        Assert.Equal(21, repository.Records.Count);
        Assert.Equal(8, repository.Fixtures.Count);
        Assert.All(repository.Documents, document => Assert.NotNull(document.Content));
        Assert.Contains(repository.Records, document => document.Identifier == "artifact:customer-agent-2.8.4");
        Assert.Contains(repository.Fixtures, document => document.RelativePath == "fixtures/github/repository-response.json");
    }

    [Fact]
    public void SerializingLoadedRepositoryTwiceProducesIdenticalBytes()
    {
        var repository = RepositoryDataLoader.Load(TestWorld.RepositoryRoot);

        var first = CanonicalJsonSerializer.Serialize(repository);
        var second = CanonicalJsonSerializer.Serialize(repository);

        Assert.Equal(first, second);
    }

    [Fact]
    public void CanonicalSerializationIgnoresPropertyAndIdentifiedCollectionOrder()
    {
        var first = JsonNode.Parse("""{"items":[{"id":"b","value":2},{"id":"a","value":1}],"name":"example"}""")!;
        var second = JsonNode.Parse("""{"name":"example","items":[{"value":1,"id":"a"},{"value":2,"id":"b"}]}""")!;

        Assert.Equal(CanonicalJsonSerializer.Serialize(first), CanonicalJsonSerializer.Serialize(second));
    }

    [Fact]
    public void FileCreationOrderDoesNotAffectLoadedOutput()
    {
        using var first = TemporaryRepository.Create(
            ("data/z.yaml", "id: node:z\nvalue: 2\n"),
            ("data/a.yaml", "id: node:a\nvalue: 1\n"),
            ("fixtures/z.json", "{\"value\":2}"),
            ("fixtures/a.json", "{\"value\":1}"));
        using var second = TemporaryRepository.Create(
            ("fixtures/a.json", "{\"value\":1}"),
            ("fixtures/z.json", "{\"value\":2}"),
            ("data/a.yaml", "id: node:a\nvalue: 1\n"),
            ("data/z.yaml", "id: node:z\nvalue: 2\n"));

        var firstBytes = CanonicalJsonSerializer.Serialize(RepositoryDataLoader.Load(first.Path));
        var secondBytes = CanonicalJsonSerializer.Serialize(RepositoryDataLoader.Load(second.Path));

        Assert.Equal(firstBytes, secondBytes);
    }

    [Theory]
    [InlineData("broken-record", "data/deliberately-broken.yaml")]
    [InlineData("broken-fixture", "fixtures/deliberately-broken.json")]
    public void MalformedFileReportsItsRepositoryRelativePath(string world, string expectedPath)
    {
        var exception = Assert.Throws<RepositoryDataException>(
            () => RepositoryDataLoader.Load(TestWorld.DerivedWorld(world)));

        Assert.Equal(expectedPath, exception.Path);
        Assert.Contains($"Unable to load '{expectedPath}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnreadableFileReportsItsRepositoryRelativePath()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            // Windows lacks Unix file modes, and root bypasses file permissions,
            // so an unreadable file cannot be simulated on either.
            return;
        }

        using var repository = TemporaryRepository.Create(("data/secret.yaml", "id: node:secret\n"));
        var path = Path.Combine(repository.Path, "data", "secret.yaml");
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            var exception = Assert.Throws<RepositoryDataException>(() => RepositoryDataLoader.Load(repository.Path));

            Assert.Equal("data/secret.yaml", exception.Path);
            Assert.Contains("Unable to load 'data/secret.yaml'", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void CanonicalRepositoryOutputMatchesGoldenFile()
    {
        var repository = RepositoryDataLoader.Load(TestWorld.RepositoryRoot);

        Golden.AssertMatches("repository-canonical.json", CanonicalJsonSerializer.Serialize(repository));
    }

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryRepository Create(params (string Path, string Content)[] files)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"compliance-traversal-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(System.IO.Path.Combine(root, "data"));
            Directory.CreateDirectory(System.IO.Path.Combine(root, "fixtures"));

            foreach (var file in files)
            {
                var path = System.IO.Path.Combine(root, file.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                File.WriteAllText(path, file.Content, new UTF8Encoding(false));
            }

            return new TemporaryRepository(root);
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
