using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Graph;
using GraphBasedComplianceTraversal.Engine.IO;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Ingestion;

/// <summary>
/// Loads the repository's organizational records (<c>data/**</c>) into a typed
/// property graph as declared facts. Every record becomes exactly one node:
/// its <c>id</c> and <c>type</c> fields map to the node's stable id and its
/// ontology type, the remaining fields become its properties, and every fact
/// carries <see cref="SourceKind.Declaration"/> provenance naming the record's
/// file. Reference fields are converted into typed edges through the
/// registry's field-name mapping (see <c>EdgeTypeRegistry.FieldMappings</c>),
/// retaining targets that have no record as unresolved references, and fields
/// the ontology declares unmodeled are recorded as
/// <see cref="UnmodeledReference"/> instances — nothing is silently dropped.
/// The observations history document (<c>security/observations/**</c>) carries
/// an <c>observations</c> list rather than a single <c>id</c>/<c>type</c>
/// pair; each entry becomes one <c>Observation</c> node linked to its
/// assertion. See <c>docs/ingestion.md</c>.
/// </summary>
public static class DeclaredRecordsLoader
{
    /// <summary>
    /// Loads every record into the graph, in document order. A record whose
    /// shape, node type, id prefix, or reference values are malformed is
    /// rejected with a <see cref="DeclaredRecordException"/> naming its path —
    /// never skipped. The graph is expected to hold only declared facts;
    /// merging them with observed facts is the reconciliation milestone's
    /// concern.
    /// </summary>
    public static void ApplyAll(TypedPropertyGraph graph, IReadOnlyList<RepositoryDocument> records)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(records);
        foreach (var record in records)
        {
            ApplyRecord(graph, record);
        }
    }

    private static void ApplyRecord(TypedPropertyGraph graph, RepositoryDocument record)
    {
        var root = record.Content as JsonObject
            ?? throw Invalid(record, "the record must be a YAML object.");

        if (TryGetString(root, "type") is string typeName && TryGetString(root, "id") is string id)
        {
            ApplyNode(graph, record, root, id, RequireNodeType(graph.Registry, record, typeName));
            return;
        }

        if (root.TryGetPropertyValue("observations", out var observations) && observations is JsonArray entries)
        {
            var observationType = RequireNodeType(graph.Registry, record, "Observation");
            foreach (var element in entries)
            {
                var entry = element as JsonObject
                    ?? throw Invalid(record, "every entry of 'observations' must be an object.");
                ApplyNode(graph, record, entry, RequireStringField(record, entry, "id"), observationType);
            }

            return;
        }

        throw Invalid(record, "a record must declare string 'id' and 'type' fields, or an 'observations' list.");
    }

    private static void ApplyNode(
        TypedPropertyGraph graph,
        RepositoryDocument record,
        JsonObject content,
        string id,
        NodeType nodeType)
    {
        RequireIdPrefix(record, nodeType, id);

        var provenance = new FactProvenance(new ProvenanceRecord(SourceKind.Declaration, record.RelativePath));
        var properties = content
            .Where(pair => pair.Key is not ("id" or "type") && pair.Value is not null)
            .Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value))
            .ToArray();

        var node = graph.AddNode(id, nodeType, properties, provenance);
        foreach (var property in properties)
        {
            node.SetPropertyProvenance(property.Key, provenance.Sources.ToArray());
        }

        ApplyEdges(graph, record, content, id, nodeType, provenance);
        ApplyUnmodeledReferences(graph, record, content, id, nodeType);
    }

    private static void ApplyEdges(
        TypedPropertyGraph graph,
        RepositoryDocument record,
        JsonObject content,
        string id,
        NodeType nodeType,
        FactProvenance provenance)
    {
        var fields = graph.Registry.FieldMappings
            .Where(mapping => mapping.SourceType.Name == nodeType.Name)
            .GroupBy(mapping => mapping.FieldPath, StringComparer.Ordinal);

        foreach (var field in fields)
        {
            foreach (var value in EnumerateFieldValues(content, field.Key))
            {
                var target = RequireStringValue(record, field.Key, value);
                var mapping = ResolveMapping(record, field.ToArray(), target);
                ApplyEdge(
                    graph,
                    mapping.CanonicalName,
                    id,
                    mapping.SourceType,
                    QualifyTargetId(mapping.TargetType, target),
                    mapping.TargetType,
                    provenance);
            }
        }
    }

    private static void ApplyEdge(
        TypedPropertyGraph graph,
        string name,
        string fromId,
        NodeType fromType,
        string toId,
        NodeType toType,
        FactProvenance provenance)
    {
        // The field vocabulary carries reciprocal fields (stored_in/contains,
        // used_by/depends_on, ...), so the same relationship is often stated
        // once per side. It stays one edge: the first statement fixes the
        // stored direction, and every later statement appends its provenance
        // record — the same merge semantics as NormalizedState.ApplyTo.
        var resolved = graph.Registry.Resolve(name, fromType, toType);
        var existing = graph.Edges.FirstOrDefault(edge =>
            edge.EdgeType == resolved.EdgeType
            && (edge.FromId == fromId && edge.ToId == toId || edge.FromId == toId && edge.ToId == fromId));
        if (existing is not null)
        {
            existing.AppendProvenance(provenance.Sources.ToArray());
            return;
        }

        graph.AddEdge(name, fromId, fromType, toId, toType, provenance);
    }

    private static void ApplyUnmodeledReferences(
        TypedPropertyGraph graph,
        RepositoryDocument record,
        JsonObject content,
        string id,
        NodeType nodeType)
    {
        var fields = graph.Registry.UnmodeledRelationships
            .Where(relationship => relationship.SourceType.Name == nodeType.Name);

        foreach (var relationship in fields)
        {
            foreach (var value in EnumerateFieldValues(content, relationship.FieldPath))
            {
                // Only string values are references; free-text fields and
                // structured values stay in the node's properties.
                if (StringOf(value) is string targetId)
                {
                    graph.RecordUnmodeledReference(id, relationship.FieldPath, targetId);
                }
            }
        }
    }

    private static NodeType RequireNodeType(EdgeTypeRegistry registry, RepositoryDocument record, string typeName) =>
        registry.NodeTypes.SingleOrDefault(type => type.Name == typeName)
        ?? throw Invalid(record, $"node type '{typeName}' is not declared by the ontology slice.");

    private static void RequireIdPrefix(RepositoryDocument record, NodeType nodeType, string id)
    {
        var prefix = nodeType.IdPrefix + ":";
        if (!id.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw Invalid(
                record,
                $"id '{id}' does not carry the '{nodeType.IdPrefix}' prefix declared for node type '{nodeType.Name}'.");
        }
    }

    /// <summary>
    /// Picks the mapping whose target type a reference value names. A value
    /// carrying a declared id prefix names that prefix's node type; a bare
    /// name (the fixtures' <c>control.specification: ARR-3-11</c>) resolves
    /// only when the field declares a single target type.
    /// </summary>
    private static RelationshipFieldMapping ResolveMapping(
        RepositoryDocument record,
        RelationshipFieldMapping[] candidates,
        string value)
    {
        var matches = candidates
            .Where(candidate => value.StartsWith(candidate.TargetType.IdPrefix + ":", StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 1)
        {
            return matches[0];
        }

        if (matches.Length == 0 && candidates.Length == 1)
        {
            return candidates[0];
        }

        throw Invalid(
            record,
            $"reference '{value}' does not name a target of any node type declared for this field.");
    }

    private static string QualifyTargetId(NodeType targetType, string value) =>
        value.StartsWith(targetType.IdPrefix + ":", StringComparison.Ordinal)
            ? value
            : targetType.IdPrefix + ":" + value;

    private static string RequireStringField(RepositoryDocument record, JsonObject obj, string field) =>
        TryGetString(obj, field)
        ?? throw Invalid(record, $"field '{field}' must be a string.");

    private static string RequireStringValue(RepositoryDocument record, string fieldPath, JsonNode value) =>
        StringOf(value)
        ?? throw Invalid(record, $"field '{fieldPath}' must reference node ids as strings.");

    private static string? TryGetString(JsonObject obj, string field) =>
        obj.TryGetPropertyValue(field, out var node) && node is JsonValue jsonValue && StringOf(jsonValue) is string text
            ? text
            : null;

    private static string? StringOf(JsonNode? value) =>
        value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// The scalar values at a possibly dotted field path (<c>subject.artifact</c>,
    /// <c>control.specification</c>): objects are descended into, a list along
    /// the path fans the remainder out over its elements
    /// (<c>mechanisms.covers</c>), and a list at the leaf holds several
    /// references to the same field (<c>capabilities</c>).
    /// </summary>
    private static IEnumerable<JsonNode> EnumerateFieldValues(JsonObject content, string fieldPath)
    {
        var segments = fieldPath.Split('.');
        IEnumerable<JsonNode> current = [content];
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            var isLast = index == segments.Length - 1;
            current = current.SelectMany(node => Step(node, segment, isLast));
        }

        return current;
    }

    private static IEnumerable<JsonNode> Step(JsonNode node, string segment, bool isLast)
    {
        if (node is JsonArray array)
        {
            return array.SelectMany(element => element is null ? [] : Step(element, segment, isLast));
        }

        if (node is JsonObject obj && obj.TryGetPropertyValue(segment, out var value) && value is not null)
        {
            return isLast && value is JsonArray leaf
                ? leaf.Where(element => element is not null).Select(element => element!)
                : [value];
        }

        return [];
    }

    private static DeclaredRecordException Invalid(RepositoryDocument record, string message) =>
        new(record.RelativePath, message);
}
