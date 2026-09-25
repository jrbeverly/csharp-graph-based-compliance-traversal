using GraphBasedComplianceTraversal.Engine.Ontology;
using GraphBasedComplianceTraversal.Engine.Provenance;

namespace GraphBasedComplianceTraversal.Engine.Graph;

/// <summary>
/// A typed edge in the property graph: a registry-validated
/// <see cref="EdgeType"/> connecting two node ids. The edge is stored in the
/// direction it was added — <see cref="FromId"/> and <see cref="ToId"/> are the
/// endpoints as given, and <see cref="Name"/> is the forward or inverse name
/// the edge was added under (<see cref="Direction"/> records which). Either
/// endpoint id may be absent from node storage; such an edge is a retained
/// unresolved reference, reported through <see cref="MissingEndpointIds"/> and
/// <see cref="IsResolved"/> rather than dropped or invented as a real node.
/// The edge carries the provenance of the relationship itself, so a derived
/// edge can answer which fixture established it.
/// </summary>
public sealed class GraphEdge
{
    private readonly TypedPropertyGraph _graph;
    private FactProvenance _provenance;

    internal GraphEdge(
        TypedPropertyGraph graph,
        EdgeType edgeType,
        string name,
        EdgeDirection direction,
        string fromId,
        string toId,
        FactProvenance? provenance)
    {
        _graph = graph;
        EdgeType = edgeType;
        Name = name;
        Direction = direction;
        FromId = fromId;
        ToId = toId;
        _provenance = provenance ?? FactProvenance.None;
    }

    /// <summary>The edge type resolved against the ontology registry at add time.</summary>
    public EdgeType EdgeType { get; }

    /// <summary>The edge name the edge was added under: the forward or inverse name.</summary>
    public string Name { get; }

    /// <summary>
    /// The direction <see cref="Name"/> was used in relative to
    /// <see cref="EdgeType"/>: <see cref="EdgeDirection.Forward"/> when the
    /// edge was added under the forward name, <see cref="EdgeDirection.Inverse"/>
    /// under the inverse name.
    /// </summary>
    public EdgeDirection Direction { get; }

    /// <summary>The id of the endpoint the edge was added from.</summary>
    public string FromId { get; }

    /// <summary>
    /// The provenance of the edge — where the relationship was established
    /// from, for example the provenance or CI fixture a derived edge was read
    /// from. <see cref="FactProvenance.None"/> for an edge whose source is
    /// not known, which is marked unknown rather than defaulted to
    /// authoritative.
    /// </summary>
    public FactProvenance Provenance => _provenance;

    /// <summary>The id of the endpoint the edge was added to.</summary>
    public string ToId { get; }

    /// <summary>The edge type's forward name; reads <see cref="FromId"/> to <see cref="ToId"/>.</summary>
    public string ForwardName => EdgeType.ForwardName;

    /// <summary>The edge type's inverse name; reads <see cref="ToId"/> to <see cref="FromId"/>.</summary>
    public string InverseName => EdgeType.InverseName;

    /// <summary>
    /// The edge's name read in the opposite direction from the one it was
    /// stored: the inverse name when the edge was added under the forward
    /// name, and the forward name when it was added under the inverse name.
    /// This is the name the edge answers from <see cref="ToId"/>.
    /// </summary>
    public string ReverseName => Name == ForwardName ? InverseName : ForwardName;

    /// <summary>
    /// The direction a traversal from <paramref name="fromId"/> across this edge
    /// takes relative to the edge type: <see cref="EdgeDirection.Forward"/> when
    /// it moves from the edge type's from-type endpoint
    /// (<see cref="EdgeType.FromType"/>) to its to-type endpoint
    /// (<see cref="EdgeType.ToType"/>), and <see cref="EdgeDirection.Inverse"/>
    /// when it moves back. The answer is independent of the direction the edge
    /// was stored in: an edge stored under its inverse name still traverses in
    /// the edge type's forward direction from its to-type endpoint.
    /// </summary>
    public EdgeDirection DirectionFrom(string fromId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromId);
        if (fromId != FromId && fromId != ToId)
        {
            throw new ArgumentException($"'{fromId}' is not an endpoint of this edge.", nameof(fromId));
        }

        var fromTypeSideId = Direction == EdgeDirection.Forward ? FromId : ToId;
        return fromId == fromTypeSideId ? EdgeDirection.Forward : EdgeDirection.Inverse;
    }

    /// <summary>
    /// The endpoint ids that have no node in the graph, computed against the
    /// current node storage — adding the node later resolves the edge.
    /// </summary>
    public IReadOnlyList<string> MissingEndpointIds => _graph.GetMissingEndpointIds(this);

    /// <summary>True when both endpoint ids are stored as nodes.</summary>
    public bool IsResolved => MissingEndpointIds.Count == 0;

    /// <summary>
    /// Appends provenance records to the edge's own provenance, skipping
    /// records already present. Used when a later source restates the same
    /// relationship (for example the attestation confirming the build-run
    /// commit the CI record already established): every source is retained,
    /// none duplicated.
    /// </summary>
    internal void AppendProvenance(params ProvenanceRecord[] records) =>
        _provenance = new FactProvenance(_provenance.Sources.Concat(records).Distinct().ToArray());
}
