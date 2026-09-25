using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Ontology;
using Xunit;

namespace GraphBasedComplianceTraversal.Tests;

/// <summary>
/// Proves the ontology slice round-trips the repository fixtures' relationship
/// vocabulary. "Virtual nodes" are the entities the fixtures describe: one per
/// typed record, plus one per entry of the observations record (which carries
/// no top-level type field). The extraction rule treats a string value as a
/// node reference when it starts with a registered node type id prefix (for
/// example <c>aws:s3:</c>), and as a reference into an unmodeled namespace
/// when it starts with a lower-case scheme (<c>team:</c>, <c>role:</c>,
/// <c>terraform:</c>) that is neither a registered prefix nor one of the
/// well-known property schemes: <c>http</c>, <c>https</c>, <c>arn</c> (resource
/// ARNs), <c>aws</c> (see the <c>aws:kms</c> encryption algorithm name), and
/// <c>sha256</c> (content digests). Fields whose values reference nothing are
/// properties.
/// </summary>
public sealed class OntologySliceFixtureRoundTripTests
{
    private static readonly string[] PropertySchemes = ["http", "https", "arn", "aws", "sha256"];

    // Node types modeled by the external-source fixtures rather than by
    // data/** records: the CI workflow-run response carries the build run and
    // its head commit, and the provenance attestation references both.
    private static readonly string[] AdapterBorneNodeTypes = ["BuildRun", "Commit"];

    [Fact]
    public void EveryRegisteredNodeTypeIsUsedByTheFixtures()
    {
        var registry = EdgeTypeRegistry.Slice;
        var used = VirtualNodes(LoadRepository())
            .Select(node => node.Type.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var type in registry.NodeTypes)
        {
            if (AdapterBorneNodeTypes.Contains(type.Name, StringComparer.Ordinal))
            {
                continue; // grounded by EveryAdapterBorneNodeTypeIsGroundedInTheExternalFixtures
            }

            Assert.Contains(type.Name, used);
        }
    }

    [Fact]
    public void EveryAdapterBorneNodeTypeIsGroundedInTheExternalFixtures()
    {
        // BuildRun: the workflow-run response carries the run id the adapter
        // keys the node by. Commit: the workflow-run response carries the head
        // commit SHA, and the commit response carries the commit's own SHA.
        var fixtures = LoadRepository().Fixtures;
        var run = Assert.IsType<JsonObject>(
            fixtures.Single(document => document.RelativePath == "fixtures/ci/workflow-run-response.json").Content);
        Assert.IsAssignableFrom<JsonValue>(run["id"]);
        Assert.IsAssignableFrom<JsonValue>(run["head_sha"]);

        var commit = Assert.IsType<JsonObject>(
            fixtures.Single(document => document.RelativePath == "fixtures/github/commit-response.json").Content);
        Assert.IsAssignableFrom<JsonValue>(commit["sha"]);
    }

    [Fact]
    public void EveryFieldMappingIsGroundedInTheFixtures()
    {
        var registry = EdgeTypeRegistry.Slice;
        var virtualNodes = VirtualNodes(LoadRepository());

        foreach (var mapping in registry.FieldMappings)
        {
            if (mapping.SourceType.Name == "ControlAdoption" && mapping.FieldPath == "control.specification")
            {
                // The adoption references its definition by specification name
                // rather than node id; the definition record names that
                // specification, so the reference resolves to control-def:{name}.
                var adoption = virtualNodes.Single(node => node.Type.Name == "ControlAdoption");
                var definition = virtualNodes.Single(node => node.Type.Name == "ControlDefinition");
                var specification = Assert.Single(StringsAt(adoption.Content, mapping.FieldPath));
                Assert.Equal("ARR-3-11", specification);
                Assert.Equal(specification, Assert.Single(StringsAt(definition.Content, "specification")));
                continue;
            }

            var prefix = mapping.TargetType.IdPrefix + ":";
            var grounded = virtualNodes
                .Where(node => node.Type.Name == mapping.SourceType.Name)
                .Any(node => StringsAt(node.Content, mapping.FieldPath).Any(
                    value => value.StartsWith(prefix, StringComparison.Ordinal)));

            Assert.True(grounded,
                $"Field mapping '{mapping.SourceType.Name}.{mapping.FieldPath}' -> '{mapping.TargetType.Name}' " +
                "does not correspond to any value in the fixtures.");
        }
    }

    [Fact]
    public void EveryUnmodeledRelationshipIsGroundedInTheFixtures()
    {
        var registry = EdgeTypeRegistry.Slice;
        var virtualNodes = VirtualNodes(LoadRepository());

        foreach (var unmodeled in registry.UnmodeledRelationships)
        {
            var grounded = virtualNodes
                .Where(node => node.Type.Name == unmodeled.SourceType.Name)
                .Any(node => StringsAt(node.Content, unmodeled.FieldPath).Any());

            Assert.True(grounded,
                $"Unmodeled relationship '{unmodeled.SourceType.Name}.{unmodeled.FieldPath}' " +
                "does not correspond to any field in the fixtures.");
        }
    }

    [Fact]
    public void EveryNodeReferenceInTheFixturesMapsToExactlyOneEdgeType()
    {
        var registry = EdgeTypeRegistry.Slice;
        var virtualNodes = VirtualNodes(LoadRepository());

        foreach (var node in virtualNodes)
        {
            foreach (var (fieldPath, value) in StringValues(node.Content))
            {
                if (IsIdentityField(fieldPath))
                {
                    continue; // the node's own identity, not a relationship
                }

                var target = registry.NodeTypes.SingleOrDefault(type =>
                    value.StartsWith(type.IdPrefix + ":", StringComparison.Ordinal));
                if (target is null)
                {
                    continue; // covered by EveryUnrecognizedReferenceIsListedAsUnmodeled
                }

                var rows = registry.FieldMappings
                    .Where(mapping =>
                        mapping.SourceType.Name == node.Type.Name
                        && mapping.FieldPath == fieldPath
                        && mapping.TargetType.Name == target.Name)
                    .ToArray();

                Assert.True(rows.Length == 1,
                    $"Node reference '{node.Type.Name}.{fieldPath}' = '{value}' must map to exactly one " +
                    $"canonical edge type but maps to {rows.Length}. Node: {node.Id ?? node.Type.Name}.");
            }
        }
    }

    [Fact]
    public void EveryUnrecognizedReferenceIsListedAsUnmodeled()
    {
        var registry = EdgeTypeRegistry.Slice;
        var virtualNodes = VirtualNodes(LoadRepository());

        foreach (var node in virtualNodes)
        {
            foreach (var (fieldPath, value) in StringValues(node.Content))
            {
                if (IsIdentityField(fieldPath))
                {
                    continue;
                }

                var isNodeReference = registry.NodeTypes.Any(type =>
                    value.StartsWith(type.IdPrefix + ":", StringComparison.Ordinal));
                if (isNodeReference)
                {
                    continue;
                }

                var colon = value.IndexOf(':');
                if (colon <= 0
                    || !IsLowerKebabPrefix(value.AsSpan(0, colon))
                    || PropertySchemes.Contains(value[..colon], StringComparer.Ordinal))
                {
                    continue; // a property, not a reference
                }

                var listed = registry.UnmodeledRelationships.Any(unmodeled =>
                    unmodeled.SourceType.Name == node.Type.Name && unmodeled.FieldPath == fieldPath);

                Assert.True(listed,
                    $"Field '{node.Type.Name}.{fieldPath}' carries the reference '{value}', which is neither a " +
                    "declared node id nor listed as intentionally unmodeled.");
            }
        }
    }

    private static RepositoryData LoadRepository() => RepositoryDataLoader.Load(TestWorld.RepositoryRoot);

    private static bool IsIdentityField(string fieldPath) =>
        fieldPath == "id" || fieldPath.EndsWith(".id", StringComparison.Ordinal);

    private static bool IsLowerKebabPrefix(ReadOnlySpan<char> value)
    {
        if (value.Length == 0 || !char.IsAsciiLetterLower(value[0]))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<VirtualNode> VirtualNodes(RepositoryData repository)
    {
        var registry = EdgeTypeRegistry.Slice;
        foreach (var record in repository.Records)
        {
            var root = Assert.IsType<JsonObject>(record.Content);

            if (root.TryGetPropertyValue("type", out var typeNode) && typeNode is JsonValue)
            {
                var typeName = typeNode.GetValue<string>();
                var nodeType = registry.NodeTypes.SingleOrDefault(type => type.Name == typeName);
                Assert.True(nodeType is not null,
                    $"Fixture record '{record.Identifier}' uses type '{typeName}', which is not declared by the ontology slice.");
                yield return new VirtualNode(nodeType!, root, record.Identifier);
                continue;
            }

            // The observations record lists its entries under a top-level key
            // rather than one file per node.
            Assert.True(root.TryGetPropertyValue("observations", out var entriesNode) && entriesNode is JsonArray,
                $"Fixture record '{record.Identifier}' declares neither a 'type' nor an 'observations' list.");
            var observationType = registry.NodeTypes.Single(type => type.Name == "Observation");
            foreach (var entry in (JsonArray)entriesNode!)
            {
                var entryObject = Assert.IsType<JsonObject>(entry);
                var entryId = entryObject["id"] is JsonValue id ? id.GetValue<string>() : null;
                yield return new VirtualNode(observationType, entryObject, entryId);
            }
        }
    }

    private static IEnumerable<(string FieldPath, string Value)> StringValues(JsonNode node) =>
        Walk(node, string.Empty);

    private static IEnumerable<(string FieldPath, string Value)> Walk(JsonNode node, string path)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    if (pair.Value is null)
                    {
                        continue;
                    }

                    var childPath = path.Length == 0 ? pair.Key : path + "." + pair.Key;
                    foreach (var value in Walk(pair.Value, childPath))
                    {
                        yield return value;
                    }
                }

                yield break;
            case JsonArray array:
                foreach (var element in array)
                {
                    if (element is null)
                    {
                        continue;
                    }

                    foreach (var value in Walk(element, path))
                    {
                        yield return value;
                    }
                }

                yield break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                yield return (path, text);
                yield break;
        }
    }

    private static IEnumerable<string> StringsAt(JsonObject root, string fieldPath)
    {
        IEnumerable<JsonNode> current = [root];
        foreach (var segment in fieldPath.Split('.'))
        {
            current = current.SelectMany(node => Descend(node, segment));
        }

        return current.SelectMany(AllStrings);
    }

    private static IEnumerable<JsonNode> Descend(JsonNode node, string segment) => node switch
    {
        JsonObject obj when obj.TryGetPropertyValue(segment, out var value) && value is not null => [value],
        JsonArray array => array.Where(element => element is not null).SelectMany(element => Descend(element!, segment)),
        _ => [],
    };

    private static IEnumerable<string> AllStrings(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj)
                {
                    if (pair.Value is null)
                    {
                        continue;
                    }

                    foreach (var value in AllStrings(pair.Value))
                    {
                        yield return value;
                    }
                }

                yield break;
            case JsonArray array:
                foreach (var element in array)
                {
                    if (element is null)
                    {
                        continue;
                    }

                    foreach (var value in AllStrings(element))
                    {
                        yield return value;
                    }
                }

                yield break;
            case JsonValue value when value.TryGetValue<string>(out var text):
                yield return text;
                yield break;
        }
    }

    private sealed record VirtualNode(NodeType Type, JsonObject Content, string? Id);
}
