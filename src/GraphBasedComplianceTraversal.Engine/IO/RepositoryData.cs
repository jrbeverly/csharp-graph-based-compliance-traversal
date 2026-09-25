namespace GraphBasedComplianceTraversal.Engine.IO;

public sealed record RepositoryData(
    IReadOnlyList<RepositoryDocument> Records,
    IReadOnlyList<RepositoryDocument> Fixtures)
{
    public IEnumerable<RepositoryDocument> Documents => Records.Concat(Fixtures);
}
