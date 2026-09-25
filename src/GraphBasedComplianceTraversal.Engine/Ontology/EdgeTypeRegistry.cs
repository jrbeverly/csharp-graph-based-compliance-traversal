namespace GraphBasedComplianceTraversal.Engine.Ontology;

/// <summary>
/// The single authoritative registry of the ontology slice: the declared node
/// types, the typed edge types, the mapping from fixture relationship fields
/// onto canonical edge types, and the intentionally unmodeled relationships.
/// <see cref="Slice"/> is the registry every component in this repository
/// compiles against. <see cref="Resolve"/> rejects any edge whose name or
/// endpoint node types are not declared.
/// </summary>
public sealed class EdgeTypeRegistry
{
    private readonly Dictionary<(string Name, string From, string To), (EdgeType Edge, EdgeDirection Direction)> _resolutions;
    private readonly HashSet<string> _nodeTypeNames;

    public EdgeTypeRegistry(
        IReadOnlyList<NodeType> nodeTypes,
        IReadOnlyList<EdgeType> edgeTypes,
        IReadOnlyList<RelationshipFieldMapping> fieldMappings,
        IReadOnlyList<UnmodeledRelationship> unmodeledRelationships)
    {
        ArgumentNullException.ThrowIfNull(nodeTypes);
        ArgumentNullException.ThrowIfNull(edgeTypes);
        ArgumentNullException.ThrowIfNull(fieldMappings);
        ArgumentNullException.ThrowIfNull(unmodeledRelationships);
        ValidateDefinitions(nodeTypes, edgeTypes, fieldMappings, unmodeledRelationships);
        NodeTypes = nodeTypes;
        EdgeTypes = edgeTypes;
        FieldMappings = fieldMappings;
        UnmodeledRelationships = unmodeledRelationships;
        _nodeTypeNames = nodeTypes.Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        _resolutions = BuildResolutions(edgeTypes);
    }

    /// <summary>The single authoritative ontology slice for this repository.</summary>
    public static EdgeTypeRegistry Slice { get; } = SliceDefinition.Create();

    /// <summary>The node types declared by the slice.</summary>
    public IReadOnlyList<NodeType> NodeTypes { get; }

    /// <summary>The typed edge types declared by the slice.</summary>
    public IReadOnlyList<EdgeType> EdgeTypes { get; }

    /// <summary>The fixture relationship field vocabulary mapped onto canonical edge types.</summary>
    public IReadOnlyList<RelationshipFieldMapping> FieldMappings { get; }

    /// <summary>The fixture relationship fields intentionally unmodeled in this slice.</summary>
    public IReadOnlyList<UnmodeledRelationship> UnmodeledRelationships { get; }

    /// <summary>
    /// Resolves a candidate edge, or throws <see cref="EdgeTypeViolationException"/>
    /// when its name or endpoint node types are not declared by the slice.
    /// </summary>
    public ResolvedEdge Resolve(string name, NodeType fromType, NodeType toType) =>
        TryResolve(name, fromType, toType, out var resolved)
            ? resolved
            : throw new EdgeTypeViolationException(name, fromType, toType, DescribeViolation(name, fromType, toType));

    /// <summary>
    /// Attempts to resolve a candidate edge. Returns <c>false</c> when its name
    /// or endpoint node types are not declared by the slice.
    /// </summary>
    public bool TryResolve(string name, NodeType fromType, NodeType toType, out ResolvedEdge resolved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(fromType);
        ArgumentNullException.ThrowIfNull(toType);

        if (_resolutions.TryGetValue((name, fromType.Name, toType.Name), out var match))
        {
            resolved = new ResolvedEdge(match.Edge, match.Direction);
            return true;
        }

        resolved = default;
        return false;
    }

    private string DescribeViolation(string name, NodeType fromType, NodeType toType)
    {
        if (!_nodeTypeNames.Contains(fromType.Name) || !_nodeTypeNames.Contains(toType.Name))
        {
            var undeclared = _nodeTypeNames.Contains(fromType.Name) ? toType.Name : fromType.Name;
            return $"Node type '{undeclared}' is not declared by the ontology slice, so edge '{name}' cannot be resolved.";
        }

        return $"No edge type named '{name}' connects '{fromType.Name}' to '{toType.Name}'. " +
            "Edge names are directional: a forward or inverse name resolves only across its declared endpoints.";
    }

    private static Dictionary<(string Name, string From, string To), (EdgeType Edge, EdgeDirection Direction)> BuildResolutions(
        IReadOnlyList<EdgeType> edgeTypes)
    {
        var resolutions = new Dictionary<(string Name, string From, string To), (EdgeType Edge, EdgeDirection Direction)>();
        foreach (var edge in edgeTypes)
        {
            AddResolution(resolutions, edge, edge.ForwardName, edge.FromType, edge.ToType, EdgeDirection.Forward, nameof(edgeTypes));
            AddResolution(resolutions, edge, edge.InverseName, edge.ToType, edge.FromType, EdgeDirection.Inverse, nameof(edgeTypes));
        }

        return resolutions;
    }

    private static void AddResolution(
        Dictionary<(string Name, string From, string To), (EdgeType Edge, EdgeDirection Direction)> resolutions,
        EdgeType edge,
        string name,
        NodeType fromType,
        NodeType toType,
        EdgeDirection direction,
        string parameterName)
    {
        var key = (name, fromType.Name, toType.Name);
        if (!resolutions.TryAdd(key, (edge, direction)))
        {
            throw new ArgumentException(
                $"Edge name '{name}' from '{fromType.Name}' to '{toType.Name}' is declared more than once.",
                parameterName);
        }
    }

    private static void ValidateDefinitions(
        IReadOnlyList<NodeType> nodeTypes,
        IReadOnlyList<EdgeType> edgeTypes,
        IReadOnlyList<RelationshipFieldMapping> fieldMappings,
        IReadOnlyList<UnmodeledRelationship> unmodeledRelationships)
    {
        if (nodeTypes.Count == 0)
        {
            throw new ArgumentException("At least one node type must be declared.", nameof(nodeTypes));
        }

        if (edgeTypes.Count == 0)
        {
            throw new ArgumentException("At least one edge type must be declared.", nameof(edgeTypes));
        }

        RequireDistinct(nodeTypes.Select(type => type.Name), "node type name", nameof(nodeTypes));
        RequireDistinct(nodeTypes.Select(type => type.IdPrefix), "node type id prefix", nameof(nodeTypes));

        var nodeTypeNames = nodeTypes.Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var edge in edgeTypes)
        {
            if (string.IsNullOrWhiteSpace(edge.ForwardName) || string.IsNullOrWhiteSpace(edge.InverseName))
            {
                throw new ArgumentException("Edge forward and inverse names must not be empty.", nameof(edgeTypes));
            }

            if (string.Equals(edge.ForwardName, edge.InverseName, StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    $"Edge '{edge.ForwardName}' must declare distinct forward and inverse names.",
                    nameof(edgeTypes));
            }

            RequireDeclared(edge.FromType, nodeTypeNames, nameof(edgeTypes), edge);
            RequireDeclared(edge.ToType, nodeTypeNames, nameof(edgeTypes), edge);
        }

        foreach (var mapping in fieldMappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.FieldPath))
            {
                throw new ArgumentException("Field mapping paths must not be empty.", nameof(fieldMappings));
            }

            RequireDeclared(mapping.SourceType, nodeTypeNames, nameof(fieldMappings), mapping);
            RequireDeclared(mapping.TargetType, nodeTypeNames, nameof(fieldMappings), mapping);

            if (!edgeTypes.Contains(mapping.Edge))
            {
                throw new ArgumentException(
                    $"Field mapping for '{mapping.SourceType.Name}.{mapping.FieldPath}' references an edge type that is not declared.",
                    nameof(fieldMappings));
            }

            var endpointsMatch = mapping.Direction == EdgeDirection.Forward
                ? mapping.Edge.FromType.Name == mapping.SourceType.Name && mapping.Edge.ToType.Name == mapping.TargetType.Name
                : mapping.Edge.ToType.Name == mapping.SourceType.Name && mapping.Edge.FromType.Name == mapping.TargetType.Name;
            if (!endpointsMatch)
            {
                throw new ArgumentException(
                    $"Field mapping for '{mapping.SourceType.Name}.{mapping.FieldPath}' is not consistent with its edge type's declared endpoints.",
                    nameof(fieldMappings));
            }
        }

        RequireDistinct(
            fieldMappings.Select(mapping => (mapping.SourceType.Name, mapping.FieldPath, mapping.TargetType.Name)),
            "field mapping (source type, field, target type)",
            nameof(fieldMappings));

        RequireDistinct(
            unmodeledRelationships.Select(relationship => (relationship.SourceType.Name, relationship.FieldPath)),
            "unmodeled relationship (source type, field)",
            nameof(unmodeledRelationships));

        foreach (var unmodeled in unmodeledRelationships)
        {
            if (!nodeTypeNames.Contains(unmodeled.SourceType.Name))
            {
                throw new ArgumentException(
                    $"Unmodeled relationship '{unmodeled.SourceType.Name}.{unmodeled.FieldPath}' references a node type that is not declared.",
                    nameof(unmodeledRelationships));
            }

            if (fieldMappings.Any(mapping =>
                mapping.SourceType.Name == unmodeled.SourceType.Name && mapping.FieldPath == unmodeled.FieldPath))
            {
                throw new ArgumentException(
                    $"Field '{unmodeled.SourceType.Name}.{unmodeled.FieldPath}' is both mapped and listed as unmodeled.",
                    nameof(unmodeledRelationships));
            }
        }
    }

    private static void RequireDeclared(NodeType nodeType, HashSet<string> nodeTypeNames, string parameterName, object owner)
    {
        if (!nodeTypeNames.Contains(nodeType.Name))
        {
            throw new ArgumentException(
                $"'{owner}' references node type '{nodeType.Name}', which is not declared.",
                parameterName);
        }
    }

    private static void RequireDistinct<T>(IEnumerable<T> values, string description, string parameterName)
    {
        var seen = new HashSet<T>();
        foreach (var value in values)
        {
            if (!seen.Add(value))
            {
                throw new ArgumentException($"The same {description} '{value}' is declared more than once.", parameterName);
            }
        }
    }
}

/// <summary>
/// The result of resolving an edge: the matched edge type and the declared
/// direction (<see cref="EdgeDirection.Forward"/> or <see cref="EdgeDirection.Inverse"/>)
/// in which it resolves.
/// </summary>
public readonly record struct ResolvedEdge(EdgeType EdgeType, EdgeDirection Direction);
