namespace GraphBasedComplianceTraversal.Engine.Ontology;

/// <summary>
/// A node type declared by the ontology slice. <see cref="IdPrefix"/> is the
/// canonical identifier prefix used by records in <c>data/**</c>; for example
/// the <c>S3Bucket</c> type carries the prefix <c>aws:s3</c>, so its fixture
/// record is named <c>aws:s3:prod-release-artifacts</c>.
/// </summary>
public sealed record NodeType(string Name, string IdPrefix, string Category, string Description);
