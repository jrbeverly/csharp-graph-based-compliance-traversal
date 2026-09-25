using System.Text.Json.Nodes;

namespace GraphBasedComplianceTraversal.Engine.IO;

public enum RepositoryDocumentFormat
{
    Yaml,
    Json,
}

public sealed record RepositoryDocument(
    string RelativePath,
    RepositoryDocumentFormat Format,
    string? Identifier,
    JsonNode Content);
