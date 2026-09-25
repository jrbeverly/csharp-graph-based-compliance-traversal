using System.Text.Json.Nodes;
using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;
using GraphBasedComplianceTraversal.Engine.Reconciliation;
using GraphBasedComplianceTraversal.Engine.State;

namespace GraphBasedComplianceTraversal.Engine.Graph;

/// <summary>
/// The in-memory typed property graph: nodes keyed by stable id with a
/// declared node type and arbitrary properties, and registry-validated typed
/// edges queryable by their forward and inverse names so traversal can move in
/// either direction.
/// <list type="bullet">
/// <item>Nodes are added with <see cref="AddNode"/>; the node type must be
/// declared by the <see cref="EdgeTypeRegistry"/> the graph was constructed
/// with, and node ids are unique.</item>
/// <item>Edges are added with <see cref="AddEdge(string, string, string)"/>
/// (both endpoints must be stored nodes) or with an explicit-type overload
/// when an endpoint id may be missing.</item>
/// <item>Every edge is validated against the registry: a name that does not
/// resolve across the endpoint node types is rejected with an
/// <see cref="EdgeTypeViolationException"/>.</item>
/// <item>Dangling references are retained, not hidden: an edge whose endpoint
/// id has no node stays queryable and reports <see cref="GraphEdge.IsResolved"/>
/// false. Relationship fields the ontology declares unmodeled are recorded as
/// <see cref="UnmodeledReference"/> instances.</item>
/// <item>Every node, edge, and property can carry provenance
/// (<see cref="FactProvenance"/>) saying where it came from; facts without a
/// recorded source are marked unknown, never defaulted to authoritative.</item>
/// <item>Declared-versus-observed reconciliations
/// (<see cref="FactReconciliation"/>) are recorded per subject property,
/// making agreement and disagreement both explicit graph information.</item>
/// </list>
/// The graph is built programmatically by the Ingestion milestone; it is an
/// in-memory structure with no persistence.
/// </summary>
public sealed class TypedPropertyGraph
{
    private readonly Dictionary<string, NodeType> _nodeTypesByName;
    private readonly Dictionary<string, GraphNode> _nodes = new(StringComparer.Ordinal);
    private readonly List<GraphNode> _nodesInInsertionOrder = [];
    private readonly Dictionary<string, List<GraphNode>> _nodesByTypeName = new(StringComparer.Ordinal);
    private readonly List<GraphEdge> _edges = [];
    private readonly Dictionary<string, List<GraphEdge>> _edgesByFromId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<GraphEdge>> _edgesByToId = new(StringComparer.Ordinal);
    private readonly List<UnmodeledReference> _unmodeledReferences = [];
    private readonly List<FactReconciliation> _reconciliations = [];

    /// <summary>Creates an empty graph validated against the given ontology registry.</summary>
    public TypedPropertyGraph(EdgeTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Registry = registry;
        _nodeTypesByName = registry.NodeTypes.ToDictionary(type => type.Name, StringComparer.Ordinal);
    }

    /// <summary>The ontology slice the graph validates nodes and edges against.</summary>
    public EdgeTypeRegistry Registry { get; }

    /// <summary>All nodes, in insertion order.</summary>
    public IReadOnlyList<GraphNode> Nodes => _nodesInInsertionOrder;

    /// <summary>All edges, in insertion order.</summary>
    public IReadOnlyList<GraphEdge> Edges => _edges;

    /// <summary>
    /// The recorded references through relationship fields the ontology slice
    /// declares unmodeled, in insertion order. These are references to
    /// entities outside the slice (teams, roles, shared risk-library entries,
    /// ...) — represented, never traversable edges.
    /// </summary>
    public IReadOnlyList<UnmodeledReference> UnmodeledReferences => _unmodeledReferences;

    /// <summary>
    /// The recorded declared-versus-observed reconciliations, in insertion
    /// order. Each record is the verdict for one property of one subject:
    /// corroboration (<see cref="ValidationState.Pass"/>) or disagreement
    /// (<see cref="ValidationState.Conflicting"/>) carrying both values and
    /// both provenances. Agreement and disagreement are both explicit graph
    /// information.
    /// </summary>
    public IReadOnlyList<FactReconciliation> Reconciliations => _reconciliations;

    /// <summary>
    /// Adds a node by its stable id. The node type must be declared by the
    /// ontology slice; a repeated id is rejected, and properties may be seeded
    /// or added later by callers that own the node's record. Provenance may be
    /// seeded here or attached per property afterwards; a node added without
    /// provenance is marked as having an unknown source.
    /// </summary>
    public GraphNode AddNode(
        string id,
        NodeType type,
        IEnumerable<KeyValuePair<string, JsonNode?>>? properties = null,
        FactProvenance? provenance = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(type);

        if (!_nodeTypesByName.TryGetValue(type.Name, out var declaredType))
        {
            throw new ArgumentException(
                $"Node type '{type.Name}' is not declared by the ontology slice.",
                nameof(type));
        }

        var node = new GraphNode(id, declaredType, properties, provenance);
        if (!_nodes.TryAdd(id, node))
        {
            throw new ArgumentException($"A node with id '{id}' already exists in the graph.", nameof(id));
        }

        _nodesInInsertionOrder.Add(node);
        AddToList(_nodesByTypeName, declaredType.Name, node);
        return node;
    }

    /// <summary>Looks up a node by id.</summary>
    public bool TryGetNode(string id, out GraphNode node)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _nodes.TryGetValue(id, out node!);
    }

    /// <summary>Looks up a node by id, or throws when no node carries the id.</summary>
    public GraphNode GetNode(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _nodes.TryGetValue(id, out var node)
            ? node
            : throw new KeyNotFoundException($"No node with id '{id}' is in the graph.");
    }

    /// <summary>All nodes of the given type, in insertion order.</summary>
    public IReadOnlyList<GraphNode> GetNodes(NodeType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _nodesByTypeName.TryGetValue(type.Name, out var nodes) ? nodes : [];
    }

    /// <summary>
    /// Adds a typed edge between two stored nodes. Both endpoint ids must
    /// exist as nodes; their types are read from node storage and the name is
    /// validated against the registry. To add an edge whose endpoint is
    /// missing (a dangling reference), use the overload with explicit node
    /// types.
    /// </summary>
    public GraphEdge AddEdge(string name, string fromId, string toId, FactProvenance? provenance = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toId);

        if (!_nodes.TryGetValue(fromId, out var fromNode))
        {
            throw MissingNodeForEdge(name, fromId, nameof(fromId));
        }

        if (!_nodes.TryGetValue(toId, out var toNode))
        {
            throw MissingNodeForEdge(name, toId, nameof(toId));
        }

        return AddEdge(name, fromId, fromNode.Type, toId, toNode.Type, provenance);
    }

    /// <summary>
    /// Adds a typed edge between two stored nodes, reading their types from
    /// the nodes themselves.
    /// </summary>
    public GraphEdge AddEdge(string name, GraphNode from, GraphNode to, FactProvenance? provenance = null)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        return AddEdge(name, from.Id, from.Type, to.Id, to.Type, provenance);
    }

    /// <summary>
    /// Adds a typed edge by endpoint id and node type. The explicit types let
    /// an edge reference a node id that is not (yet) stored: the edge is
    /// validated against the registry like any other and retained as an
    /// unresolved reference, reported through <see cref="GraphEdge.MissingEndpointIds"/>.
    /// When an endpoint id is stored as a node, the given type must match the
    /// stored node's type. Provenance may be attached so the edge can answer
    /// which source (for example a provenance or CI fixture) established it;
    /// an edge added without provenance is marked as having an unknown
    /// source.
    /// </summary>
    public GraphEdge AddEdge(
        string name,
        string fromId,
        NodeType fromType,
        string toId,
        NodeType toType,
        FactProvenance? provenance = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(fromId);
        ArgumentException.ThrowIfNullOrWhiteSpace(toId);
        ArgumentNullException.ThrowIfNull(fromType);
        ArgumentNullException.ThrowIfNull(toType);

        fromType = RequireConsistentEndpointType(fromId, fromType, nameof(fromType));
        toType = RequireConsistentEndpointType(toId, toType, nameof(toType));

        // Rejects type-incompatible endpoints with an EdgeTypeViolationException.
        var resolved = Registry.Resolve(name, fromType, toType);

        var edge = new GraphEdge(this, resolved.EdgeType, name, resolved.Direction, fromId, toId, provenance);
        _edges.Add(edge);
        AddToList(_edgesByFromId, fromId, edge);
        AddToList(_edgesByToId, toId, edge);
        return edge;
    }

    /// <summary>
    /// The edges leaving <paramref name="fromId"/> under <paramref name="name"/>:
    /// edges added from <paramref name="fromId"/> under the name itself, and
    /// edges added <em>to</em> <paramref name="fromId"/> under the edge type's
    /// other name (<see cref="GraphEdge.ReverseName"/>). A single stored edge
    /// is therefore answered both by the forward query from its source and by
    /// the inverse query from its target, so traversal can move in either
    /// direction. <paramref name="fromId"/> may be a missing id, which returns
    /// the edges referencing it from their other endpoint.
    /// </summary>
    public IReadOnlyList<GraphEdge> GetEdges(string fromId, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var results = new List<GraphEdge>();
        if (_edgesByFromId.TryGetValue(fromId, out var outgoing))
        {
            results.AddRange(outgoing.Where(edge => edge.Name == name));
        }

        if (_edgesByToId.TryGetValue(fromId, out var incoming))
        {
            results.AddRange(incoming.Where(edge => edge.ReverseName == name));
        }

        return results;
    }

    /// <summary>
    /// Every edge touching <paramref name="fromId"/> — stored from it, or
    /// stored to it (readable back under its other name) — in insertion order
    /// without duplicates.
    /// </summary>
    public IReadOnlyList<GraphEdge> GetEdges(string fromId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromId);

        var results = new List<GraphEdge>();
        if (_edgesByFromId.TryGetValue(fromId, out var outgoing))
        {
            results.AddRange(outgoing);
        }

        if (_edgesByToId.TryGetValue(fromId, out var incoming))
        {
            results.AddRange(incoming.Where(edge => !results.Contains(edge)));
        }

        return results;
    }

    /// <summary>Every edge of the given edge type, in insertion order.</summary>
    public IReadOnlyList<GraphEdge> GetEdges(EdgeType edgeType)
    {
        ArgumentNullException.ThrowIfNull(edgeType);
        return _edges.Where(edge => edge.EdgeType == edgeType).ToArray();
    }

    /// <summary>
    /// The edges referencing a missing node id, computed against the current
    /// node storage — an edge disappears from this list once its missing
    /// endpoint is added as a node.
    /// </summary>
    public IReadOnlyList<GraphEdge> GetUnresolvedEdges() =>
        _edges.Where(edge => !edge.IsResolved).ToArray();

    /// <summary>
    /// Records a reference through a relationship field the ontology slice
    /// declares unmodeled (see <c>EdgeTypeRegistry.UnmodeledRelationships</c>),
    /// for example the fixtures' <c>generic_risk: ARR-RISK-044</c> or an
    /// <c>owner</c> pointing at a team or role. The reference is retained and
    /// queryable rather than silently dropped; it is never an edge. The
    /// referencing node must exist, and the field must be declared unmodeled
    /// for its type — a field mapped to an edge type belongs in
    /// <see cref="AddEdge(string, string, string)"/> instead.
    /// </summary>
    public UnmodeledReference RecordUnmodeledReference(string fromId, string fieldPath, string targetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);

        if (!_nodes.TryGetValue(fromId, out var fromNode))
        {
            throw new ArgumentException(
                $"Node '{fromId}' is not in the graph, so a reference cannot be recorded from it.",
                nameof(fromId));
        }

        var declaration = Registry.UnmodeledRelationships.SingleOrDefault(relationship =>
            relationship.SourceType.Name == fromNode.Type.Name && relationship.FieldPath == fieldPath);
        if (declaration is null)
        {
            var mapped = Registry.FieldMappings.Any(mapping =>
                mapping.SourceType.Name == fromNode.Type.Name && mapping.FieldPath == fieldPath);
            throw new ArgumentException(
                mapped
                    ? $"Field '{fieldPath}' on '{fromNode.Type.Name}' is mapped to a typed edge; add an edge instead of recording an unmodeled reference."
                    : $"Field '{fieldPath}' on '{fromNode.Type.Name}' is not declared as an unmodeled relationship by the ontology slice.",
                nameof(fieldPath));
        }

        var reference = new UnmodeledReference(fromId, fromNode.Type, fieldPath, targetId, declaration.Reason);
        _unmodeledReferences.Add(reference);
        return reference;
    }

    /// <summary>The unmodeled references recorded from the given node, in insertion order.</summary>
    public IReadOnlyList<UnmodeledReference> GetUnmodeledReferences(string fromId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromId);
        return _unmodeledReferences.Where(reference => reference.FromId == fromId).ToArray();
    }

    /// <summary>
    /// Records the declared-versus-observed reconciliation of one property of
    /// a stored node. A reconciliation identical to an already recorded one —
    /// same subject, property, state, and provenance on both sides — is not
    /// recorded twice: re-running reconciliation over an already reconciled
    /// graph never duplicates a record, matching the merge semantics of
    /// <c>NormalizedState.ApplyTo</c>. The verdict never touches the node's
    /// property: recording a reconciliation never overwrites a value.
    /// </summary>
    public FactReconciliation RecordReconciliation(FactReconciliation reconciliation)
    {
        ArgumentNullException.ThrowIfNull(reconciliation);

        if (!_nodes.ContainsKey(reconciliation.SubjectId))
        {
            throw new ArgumentException(
                $"Node '{reconciliation.SubjectId}' is not in the graph, so a reconciliation cannot be recorded for it.",
                nameof(reconciliation));
        }

        if (!_reconciliations.Any(existing => SameFact(existing, reconciliation)))
        {
            _reconciliations.Add(reconciliation);
        }

        return reconciliation;
    }

    /// <summary>The reconciliations recorded for the given subject, in insertion order.</summary>
    public IReadOnlyList<FactReconciliation> GetReconciliations(string subjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        return _reconciliations.Where(reconciliation => reconciliation.SubjectId == subjectId).ToArray();
    }

    private static bool SameFact(FactReconciliation left, FactReconciliation right) =>
        left.SubjectId == right.SubjectId
        && left.PropertyName == right.PropertyName
        && left.State == right.State
        && left.DeclaredProvenance.Sources.SequenceEqual(right.DeclaredProvenance.Sources)
        && left.ObservedProvenance.Sources.SequenceEqual(right.ObservedProvenance.Sources);

    /// <summary>The endpoint ids of an edge that have no stored node.</summary>
    internal IReadOnlyList<string> GetMissingEndpointIds(GraphEdge edge)
    {
        var missing = new List<string>();
        if (!_nodes.ContainsKey(edge.FromId))
        {
            missing.Add(edge.FromId);
        }

        if (!_nodes.ContainsKey(edge.ToId))
        {
            missing.Add(edge.ToId);
        }

        return missing;
    }

    private NodeType RequireConsistentEndpointType(string id, NodeType type, string parameterName)
    {
        if (_nodes.TryGetValue(id, out var node))
        {
            if (node.Type.Name != type.Name)
            {
                throw new ArgumentException(
                    $"Node '{id}' already exists with type '{node.Type.Name}', not '{type.Name}'.",
                    parameterName);
            }

            return node.Type;
        }

        // The endpoint id is not stored: the given type is the caller's
        // expectation. Registry.Resolve rejects undeclared types; declared
        // ones are canonicalized to the registry's instance.
        return _nodeTypesByName.TryGetValue(type.Name, out var declared) ? declared : type;
    }

    private static ArgumentException MissingNodeForEdge(string name, string missingId, string parameterName) =>
        new(
            $"Node '{missingId}' is not in the graph, so its type cannot be determined for edge '{name}'. " +
            "Pass explicit node types to retain the edge as an unresolved reference.",
            parameterName);

    private static void AddToList<TKey, TValue>(Dictionary<TKey, List<TValue>> index, TKey key, TValue value)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out var list))
        {
            index[key] = list = [];
        }

        list.Add(value);
    }
}
